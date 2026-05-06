// -----------------------------------------------------------------------------
//
// Connection Manager - Data Layer (Layer 3)
// Manages WebSocket connections, ACK handlers, command dispatching, reconnection logic
//
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Domain.Commands;
using Gamania.GIMChat.Internal.Domain.Log;
using Gamania.GIMChat.Internal.Domain.Reconnection;
using Gamania.GIMChat.Internal.Domain.TokenRefresh;
using Gamania.GIMChat.Internal.Platform.Unity.Network;
using Gamania.GIMChat.Internal.Data.Mappers;
using Gamania.GIMChat.Internal.Domain.Mappers;
using Gamania.GIMChat.Internal.Data.Repositories;
using Gamania.GIMChat.Internal.Platform;
using Gamania.GIMChat;

namespace Gamania.GIMChat.Internal.Data.Network
{
    /// <summary>
    /// Connection Manager - Manages WebSocket connection lifecycle
    ///
    /// Responsibilities:
    /// - Manage IWebSocketClient instance
    /// - ACK handlers management
    /// - Command dispatching (using EventProcessor)
    /// </summary>
    internal class ConnectionManager : IDisposable
    {
        // WebSocket client
        private IWebSocketClient _webSocketClient;

        // ACK management
        private readonly Dictionary<string, PendingAck> _pendingAcks = new Dictionary<string, PendingAck>();
        private readonly object _ackLock = new object();
        private readonly TimeSpan _defaultAckTimeout = TimeSpan.FromSeconds(5);
        private readonly ICommandProtocol _commandProtocol = new CommandProtocol();

        // Auth management
        private CancellationTokenSource _authTimeoutCts;
        private readonly TimeSpan _authTimeout = TimeSpan.FromSeconds(10);

        // Reconnection management
        private bool _isIntentionalDisconnect = false;
        private bool _isReconnecting = false;
        private bool _isDisposed = false;
        private CancellationTokenSource _reconnectionCts;
        private readonly ReconnectionManager _reconnectionManager;
        private readonly TokenRefreshManager _tokenRefreshManager;
        private WebSocketConfig _lastConfig;

        // Command processing
        private readonly WebSocketEventProcessor _eventProcessor = new WebSocketEventProcessor();

        // Login handler (for initial connection callback)
        private Action<GimUser, GimException> _loginHandler;

        /// <summary>
        /// Current connection state
        /// </summary>
        public GimConnectionState State => _webSocketClient?.State ?? GimConnectionState.Closed;

        /// <summary>
        /// Check if connection is open
        /// </summary>
        public bool IsConnected => State.IsConnected();

        /// <summary>
        /// Session key from LOGI response
        /// </summary>
        public string SessionKey { get; private set; }

        /// <summary>
        /// Event triggered when a broadcast command is received (MESG, MEDI, etc.)
        /// Upper layer (GIMChatMain) is responsible for parsing and handling
        /// </summary>
        public event Action<CommandType, string> OnBroadcastCommandReceived;

        /// <summary>
        /// Event triggered when authentication succeeds
        /// </summary>
        public event Action<string> OnAuthenticated;

        #region Connection Events

        public event Action<string> OnConnectedEvent;
        public event Action<string> OnDisconnectedEvent;
        public event Action OnReconnectStarted;
        public event Action OnReconnectSucceeded;
        public event Action OnReconnectFailed;

        #endregion

        #region Token Refresh Events (forwarded from TokenRefreshManager)

        /// <summary>
        /// Event triggered when SDK needs a new token from App.
        /// App should call provideToken(newAccessToken) when ready.
        /// </summary>
        public event Action<Action<string>> OnTokenRefreshRequired;

        /// <summary>
        /// Event triggered when token refresh succeeded
        /// </summary>
        public event Action OnSessionRefreshed;

        /// <summary>
        /// Event triggered when token refresh failed (timeout, error, etc.)
        /// </summary>
        public event Action<GimException> OnSessionDidHaveError;

        // Forward methods for TokenRefreshManager events
        private void ForwardTokenRefreshRequired(Action<string> provideToken) => OnTokenRefreshRequired?.Invoke(provideToken);
        private void ForwardSessionRefreshed() => OnSessionRefreshed?.Invoke();
        private void ForwardSessionDidHaveError(GimException error) => OnSessionDidHaveError?.Invoke(error);

        #endregion

        // Token refresh state
        private bool _isTokenRefreshing;
        private Action<GimUser, GimException> _tokenRefreshCallback;

        public ConnectionManager(IWebSocketClient webSocketClient)
            : this(webSocketClient, new TokenRefreshConfig())
        {
        }

        public ConnectionManager(IWebSocketClient webSocketClient, TokenRefreshConfig tokenRefreshConfig)
        {
            _webSocketClient = webSocketClient ?? throw new ArgumentNullException(nameof(webSocketClient));

            // Initialize reconnection manager with default policy
            var policy = new ReconnectionPolicy(
                initialDelay: 1.0f,
                backoffMultiplier: 2.0f,
                maxDelay: 30.0f,
                maxRetries: 3
            );
            _reconnectionManager = new ReconnectionManager(policy);

            // Initialize token refresh manager and forward its events
            _tokenRefreshManager = new TokenRefreshManager(tokenRefreshConfig ?? new TokenRefreshConfig());
            _tokenRefreshManager.OnTokenRefreshRequired += ForwardTokenRefreshRequired;
            _tokenRefreshManager.OnSessionRefreshed += ForwardSessionRefreshed;
            _tokenRefreshManager.OnSessionDidHaveError += ForwardSessionDidHaveError;
            _tokenRefreshManager.OnNewTokenReceived += HandleNewTokenReceived;

            RegisterWebSocketEventHandlers();
            RegisterDefaultCommandHandlers();
        }

        /// <summary>
        /// Connect to WebSocket server
        /// </summary>
        public void Connect(
            string userId,
            string authToken,
            WebSocketConfig config,
            Action<GimUser, GimException> callback)
        {
            if (string.IsNullOrEmpty(userId))
            {
                var error = new GimException(GimErrorCode.InvalidParameter, "userId cannot be null or empty");
                Logger.Error(LogCategory.Connection, "Invalid userId", error);
                callback?.Invoke(null, error);
                return;
            }

            _isIntentionalDisconnect = false;

            // Store connection config for reconnection
            _lastConfig = config;

            // Store login handler
            _loginHandler = callback;

            // Start WebSocket connection
            Logger.Info(LogCategory.Connection, $"Starting connection for user: {userId}");

            _webSocketClient.Connect(config);
        }

        /// <summary>
        /// Disconnect from WebSocket server
        /// </summary>
        public void Disconnect()
        {
            Logger.Info(LogCategory.Connection, "Disconnecting WebSocket (intentional)");

            _isIntentionalDisconnect = true;

            // Clear login handler
            _loginHandler = null;

            CancelAuthTimeout();
            CancelReconnection();

            // Clear all pending ACKs
            ClearAllPendingAcks();

            // Disconnect WebSocket
            _webSocketClient?.Disconnect();
        }

        /// <summary>
        /// Send command through WebSocket with ACK handling
        /// </summary>
        public async Task<string> SendCommandAsync(
            CommandType commandType,
            object payload,
            TimeSpan? ackTimeout = null,
            CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                throw new GimException(GimErrorCode.WebSocketConnectionClosed, "Not connected to server");
            }
            
            var (reqId, serialized) = _commandProtocol.BuildCommand(commandType, payload);

            // If command doesn't require ACK, send immediately and return
            if (!commandType.IsAckRequired())
            {
                await _webSocketClient.SendAsync(serialized);
                return null;
            }
            
            // Create task completion source for ACK
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeout = ackTimeout ?? _defaultAckTimeout;
            timeoutCts.CancelAfter(timeout);

            // Register pending ACK
            RegisterPendingAck(reqId, tcs, timeoutCts);

            try
            {
                await _webSocketClient.SendAsync(serialized);
            }
            catch
            {
                // If send fails immediately, clean up pending ACK
                CompletePendingAck(reqId, null, cancelTimeout: true);
                throw;
            }

            // Register timeout callback
            timeoutCts.Token.Register(() =>
            {
                CompletePendingAck(reqId, null, cancelTimeout: false);
            });

            return await tcs.Task;
        }

        /// <summary>
        /// Update method - must be called from Unity Update loop
        /// </summary>
        public void Update()
        {
            _webSocketClient?.Update();
            _tokenRefreshManager?.Update();
        }

        #region WebSocket Event Handlers

        private void RegisterWebSocketEventHandlers()
        {
            _webSocketClient.OnConnected += HandleWebSocketConnected;
            _webSocketClient.OnDisconnected += HandleWebSocketDisconnected;
            _webSocketClient.OnError += HandleWebSocketError;
            _webSocketClient.OnMessageReceived += HandleWebSocketMessageReceived;
        }

        private void HandleWebSocketConnected()
        {
            Logger.Info(LogCategory.Connection, "WebSocket connected (OnConnected event)");

            // Reset reconnection manager on successful connection
            _reconnectionManager.OnConnectionSuccess();

            // Start waiting for LOGI
            StartAuthTimeout();
        }

        private void HandleWebSocketDisconnected()
        {
            Logger.Info(LogCategory.Connection, "WebSocket disconnected (normal closure)");

            // Clean up resources
            CancelAuthTimeout();
            ClearAllPendingAcks();
            
            // Special case: if still in authentication phase, treat any disconnection as error
            // The server may close connection with Normal code for auth failures
            if (_loginHandler != null)
            {
                var error = new GimException(
                    GimErrorCode.WebSocketConnectionFailed,
                    "Connection closed by server during authentication"
                );
                Logger.Warning(LogCategory.Connection, "Authentication failed - connection closed by server");
                _loginHandler.Invoke(null, error);
                _loginHandler = null;
                return;
            }
            
            // Trigger disconnected event
            var userId = _lastConfig?.UserId;
            if (!string.IsNullOrEmpty(userId))
            {
                OnDisconnectedEvent?.Invoke(userId);
            }

            // Reset intentional disconnect flag
            _isIntentionalDisconnect = false;

            // No reconnection for normal closures
        }

        private void HandleWebSocketError(GimException error)
        {
            Logger.Error(LogCategory.Connection, $"WebSocket error: {error.ErrorCode}");

            CancelAuthTimeout();
            ClearAllPendingAcks();

            // If in authentication phase, notify user with real error
            if (_loginHandler != null)
            {
                Logger.Warning(LogCategory.Connection, $"Connection failed during authentication: {error.ErrorCode}");
                _loginHandler.Invoke(null, error);
                _loginHandler = null;
                return;  // No auto-reconnect for auth failures
            }

            // Auth errors should trigger token refresh flow
            if (_tokenRefreshManager.IsTokenRefreshTrigger(error))
            {
                Logger.Warning(LogCategory.Connection, "Auth error received, triggering refresh flow");
                _webSocketClient?.Disconnect();
                _tokenRefreshManager.RequestRefresh();
                return;
            }

            // Trigger disconnected event
            var userId = _lastConfig?.UserId;
            if (!string.IsNullOrEmpty(userId))
            {
                OnDisconnectedEvent?.Invoke(userId);
            }

            // Already authenticated but connection lost - attempt reconnection
            if (_reconnectionManager.ShouldAttemptReconnect(error))
            {
                Logger.Info(LogCategory.Connection, $"Scheduling reconnection attempt #{_reconnectionManager.CurrentAttempt + 1}");
                _isReconnecting = true;
                OnReconnectStarted?.Invoke();
                AttemptReconnection();
            }
            else
            {
                Logger.Warning(LogCategory.Connection, "Reconnection not attempted - max retries reached or non-retriable error");
                _isReconnecting = false;
                OnReconnectFailed?.Invoke();
            }
        }

        private void HandleWebSocketMessageReceived(string message)
        {
            try 
            {
                var commandType = CommandParser.ExtractCommandType(message);
                if (commandType == null)
                {
                    Logger.Warning(LogCategory.Connection, $"Failed to parse command type from: {message}");
                    return;
                }

                var payload = CommandParser.ExtractPayload(message);

                // Forward to event processor
                _eventProcessor.ProcessCommand(commandType.Value, payload);
            }
            catch (Exception ex)
            {
                Logger.Error(LogCategory.Connection, "Error processing message", ex);
            }
        }

        #endregion

        #region Command Handlers

        private void RegisterDefaultCommandHandlers()
        {
            _eventProcessor.RegisterHandler(CommandType.LOGI, HandleLogiCommand);
            _eventProcessor.RegisterHandler(CommandType.MESG, HandleMesgCommand);
            _eventProcessor.RegisterHandler(CommandType.FILE, HandleFileCommand);
            _eventProcessor.RegisterHandler(CommandType.MEDI, HandleMediCommand);
            _eventProcessor.RegisterHandler(CommandType.EROR, HandleErorCommand);
            _eventProcessor.RegisterHandler(CommandType.PONG, HandlePongCommand);
            _eventProcessor.RegisterHandler(CommandType.EXPR, HandleExprCommand);

            // Default handler for unregistered commands
            _eventProcessor.SetDefaultHandler((type, payload) =>
            {
                Logger.Debug(LogCategory.Command, $"Unhandled command type: {type}");
            });
        }
        
        private void HandleLogiCommand(string payload)
        {
            var logi = CommandParser.ParseLogiCommand(payload);
            if (logi != null)
            {
                Logger.Debug(LogCategory.Command, $"LOGI parsed - SessionKey: {logi.SessionKey}, Error: {logi.Error}");

                if (logi.IsSuccess())
                {
                    SessionKey = logi.SessionKey;

                    Logger.Info(LogCategory.Connection, $"Authentication successful with session key: {SessionKey}");
                    CancelAuthTimeout();

                    // Trigger OnAuthenticated BEFORE user callback
                    OnAuthenticated?.Invoke(SessionKey);

                    // Trigger connection/reconnection events
                    var userId = _webSocketClient.Config.UserId;
                    if (_isReconnecting)
                    {
                        _isReconnecting = false;
                        OnReconnectSucceeded?.Invoke();
                    }
                    OnConnectedEvent?.Invoke(userId);

                    // Invoke login handler (first connection)
                    if (_loginHandler != null)
                    {
                        // Build user from LOGI response
                        var user = new GimUser
                        {
                            UserId = logi.UserId ?? userId,
                            Nickname = logi.GetNickname(),
                            ProfileUrl = logi.GetProfileUrl()
                        };
                        _loginHandler.Invoke(user, null);
                        _loginHandler = null;
                    }
                }
                else
                {
                    Logger.Error(LogCategory.Connection, "LOGI authentication failed");
                    CancelAuthTimeout();
                    
                    var error = new GimException(GimErrorCode.ErrInvalidSession, "Authentication failed (LOGI error).");
                    
                    if (_loginHandler != null)
                    {
                        _loginHandler.Invoke(null, error);
                        _loginHandler = null;
                    }
                }
            }
            else
            {
                Logger.Error(LogCategory.Command, $"Failed to parse LOGI command from payload: {payload}");
            }
        }

        private void HandleMesgCommand(string payload)
        {
            Logger.Debug(LogCategory.Command, $"[MESG RAW] {payload}");

            var reqId = ExtractReqId(payload);

            // Check if this is an ACK response (has req_id)
            if (!string.IsNullOrWhiteSpace(reqId))
            {
                // This is an ACK for a message we sent
                bool completed = CompletePendingAck(reqId, payload, cancelTimeout: true);
                if (!completed)
                {
                    // If not found in pending ACKs, it might be a broadcast message that just happens to have an ID
                    // or a duplicate ACK. We still want to process it as a new message if it's not an ACK we're waiting for.
                }
                // Do NOT return - continue to trigger handler
                // Server sends MESG with req_id as both ACK and broadcast
            }

            // Trigger broadcast event for upper layer to handle
            OnBroadcastCommandReceived?.Invoke(CommandType.MESG, payload);
        }

        private void HandleFileCommand(string payload)
        {
            Logger.Debug(LogCategory.Command, $"[FILE RAW] {payload}");

            var reqId = ExtractReqId(payload);

            if (!string.IsNullOrWhiteSpace(reqId))
            {
                // Always complete the ACK on the first FILE response.
                // The server does NOT re-send FILE via WebSocket when processing completes.
                // The upper layer (MessageRepositoryImpl) polls via HTTP to wait for ready.
                CompletePendingAck(reqId, payload, cancelTimeout: true);
            }

            OnBroadcastCommandReceived?.Invoke(CommandType.FILE, payload);
        }

        private void HandleMediCommand(string payload)
        {
            var reqId = ExtractReqId(payload);

            // Check if this is an ACK response
            if (!string.IsNullOrWhiteSpace(reqId))
            {
                CompletePendingAck(reqId, payload, cancelTimeout: true);
            }

            // Trigger broadcast event for upper layer to handle
            Logger.Debug(LogCategory.Command, $"MEDI message updated: {payload}");
            OnBroadcastCommandReceived?.Invoke(CommandType.MEDI, payload);
        }

        private void HandleErorCommand(string payload)
        {
            var reqId = ExtractReqId(payload);
            if (!string.IsNullOrWhiteSpace(reqId))
            {
                // EROR with req_id means command failed, complete pending ACK with null
                bool completed = CompletePendingAck(reqId, null, cancelTimeout: true);
                if (completed)
                {
                    Logger.Warning(LogCategory.Command, $"EROR received for reqId: {reqId}, payload: {payload}");
                    return;
                }
            }
            
            // Broadcast EROR to upper layer for handling
            OnBroadcastCommandReceived?.Invoke(CommandType.EROR, payload);
        }

        private void HandlePongCommand(string payload)
        {
            // Silently handle PONG (heartbeat response)
            // TODO: Implement heartbeat timeout detection in future HeartbeatManager
        }

        /// <summary>
        /// Handle EXPR command - Token Expired. Triggers token refresh flow.
        /// </summary>
        private void HandleExprCommand(string payload)
        {
            Logger.Warning(LogCategory.Connection, "EXPR received - Token expired, triggering refresh flow");
            
            // Disconnect current connection
            _webSocketClient?.Disconnect();
            
            // Start token refresh via TokenRefreshManager (handles dedup + timeout)
            // External code should subscribe to TokenRefresh.OnTokenRefreshRequired
            _tokenRefreshManager.RequestRefresh();
        }

        #endregion

        #region Token Refresh

        /// <summary>
        /// Check if token should be refreshed proactively.
        /// If token expires within threshold, triggers refresh flow.
        /// </summary>
        /// <param name="currentToken">Current access token to check</param>
        /// <returns>True if proactive refresh was triggered</returns>
        public bool CheckProactiveRefresh(string currentToken)
        {
            if (string.IsNullOrEmpty(currentToken))
            {
                return false;
            }

            if (_tokenRefreshManager.IsRefreshing || _isTokenRefreshing)
            {
                return false;
            }

            if (_tokenRefreshManager.ShouldRefreshProactively(currentToken))
            {
                Logger.Info(LogCategory.Connection, "Proactive token refresh triggered");
                _webSocketClient?.Disconnect();
                _tokenRefreshManager.RequestRefresh();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Called when App provides new token via TokenRefreshManager.OnNewTokenReceived
        /// </summary>
        private void HandleNewTokenReceived(string newToken)
        {
            Logger.Info(LogCategory.Connection, "New token received from App, starting reconnection");
            
            // Reconnect with new token
            ReconnectWithToken(newToken, (user, error) =>
            {
                if (error == null)
                {
                    // Notify TokenRefreshManager that refresh completed
                    _tokenRefreshManager.CompleteRefresh();
                }
                else
                {
                    // Reset TokenRefreshManager state on failure
                    _tokenRefreshManager.Reset();
                }
            });
        }

        /// <summary>
        /// Reconnects with a new access token provided by the app via TokenRefreshManager.
        /// </summary>
        /// <param name="newAccessToken">New access token from App</param>
        /// <param name="callback">Callback when refresh completes</param>
        public void ReconnectWithToken(string newAccessToken, Action<GimUser, GimException> callback)
        {
            if (string.IsNullOrEmpty(newAccessToken))
            {
                var error = new GimException(GimErrorCode.SessionKeyRefreshFailed, "Access token cannot be null or empty");
                Logger.Error(LogCategory.Connection, "Token refresh failed: empty token", error);
                _tokenRefreshManager.FailRefresh(error);
                callback?.Invoke(null, error);
                return;
            }

            if (_lastConfig == null)
            {
                var error = new GimException(GimErrorCode.SessionKeyRefreshFailed, "No previous connection config available");
                Logger.Error(LogCategory.Connection, "Token refresh failed: no config", error);
                _tokenRefreshManager.FailRefresh(error);
                callback?.Invoke(null, error);
                return;
            }

            Logger.Info(LogCategory.Connection, "Starting token refresh reconnection");
            _isTokenRefreshing = true;

            // Create new config with updated token
            var refreshConfig = new WebSocketConfig
            {
                ApplicationId = _lastConfig.ApplicationId,
                UserId = _lastConfig.UserId,
                AccessToken = newAccessToken,  // Use new token
                EnvironmentDomain = _lastConfig.EnvironmentDomain,
                CustomWebSocketBaseUrl = _lastConfig.CustomWebSocketBaseUrl,
                AppVersion = _lastConfig.AppVersion,
                SdkVersion = _lastConfig.SdkVersion,
                SdkModule = _lastConfig.SdkModule,
                ApiVersion = _lastConfig.ApiVersion,
                PlatformVersion = _lastConfig.PlatformVersion,
                ConnectionTimeout = _lastConfig.ConnectionTimeout
            };

            // Store callback for when LOGI is received
            _tokenRefreshCallback = (user, error) =>
            {
                _isTokenRefreshing = false;
                
                if (error == null)
                {
                    // Update stored config with new token
                    _lastConfig = refreshConfig;
                    Logger.Info(LogCategory.Connection, "Token refresh reconnection successful");
                }
                else
                {
                    Logger.Error(LogCategory.Connection, "Token refresh reconnection failed", error);
                    _tokenRefreshManager.FailRefresh(error);
                }
                
                // Callback notifies HandleNewTokenReceived which calls CompleteRefresh/Reset
                callback?.Invoke(user, error);
            };

            // Set as login handler to receive LOGI response
            _loginHandler = _tokenRefreshCallback;

            // Connect with new token
            _webSocketClient.Connect(refreshConfig);
        }

        /// <summary>
        /// Check if token refresh is in progress
        /// </summary>
        public bool IsTokenRefreshing => _isTokenRefreshing || _tokenRefreshManager.IsRefreshing;

        /// <summary>
        /// Reset token refresh state (for testing and timeout scenarios)
        /// </summary>
        public void ResetTokenRefresh()
        {
            _isTokenRefreshing = false;
            _tokenRefreshManager.Reset();
        }

        #endregion

        #region ACK Management

        /// <summary>
        /// Re-registers an ACK listener for a given reqId without sending a command.
        /// Used when server will re-send a response for the same reqId (e.g., FILE not ready → wait for ready).
        /// </summary>
        public async Task<string> WaitForAckAsync(
            string reqId,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(reqId))
                throw new ArgumentException("reqId cannot be null or empty", nameof(reqId));

            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            RegisterPendingAck(reqId, tcs, timeoutCts);

            timeoutCts.Token.Register(() =>
            {
                CompletePendingAck(reqId, null, cancelTimeout: false);
            });

            return await tcs.Task;
        }

        private void RegisterPendingAck(string reqId, TaskCompletionSource<string> tcs, CancellationTokenSource timeoutCts)
        {
            lock (_ackLock)
            {
                if (_pendingAcks.ContainsKey(reqId))
                {
                    throw new InvalidOperationException($"Duplicate reqId registration: {reqId}");
                }
                _pendingAcks.Add(reqId, new PendingAck { RequestId = reqId, TaskCompletionSource = tcs, TimeoutCancellation = timeoutCts });
            }
        }

        private bool CompletePendingAck(string reqId, string ackPayload, bool cancelTimeout)
        {
            PendingAck ack;
            lock (_ackLock)
            {
                if (!_pendingAcks.TryGetValue(reqId, out ack))
                {
                    return false;
                }
                _pendingAcks.Remove(reqId);
            }

            if (cancelTimeout && ack.TimeoutCancellation != null)
            {
                ack.TimeoutCancellation.Cancel();
            }

            ack.TaskCompletionSource.TrySetResult(ackPayload);
            return true;
        }

        private void ClearAllPendingAcks()
        {
            lock (_ackLock)
            {
                var pendingAcks = new List<PendingAck>(_pendingAcks.Values);
                _pendingAcks.Clear();

                foreach (var ack in pendingAcks)
                {
                    try
                    {
                        ack.TimeoutCancellation?.Cancel();
                        var error = new GimException(GimErrorCode.WebSocketConnectionClosed, "Connection closed");
                        ack.TaskCompletionSource.TrySetException(error);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(LogCategory.Connection, "Failed to cancel pending ACK", ex);
                    }
                }

                Logger.Debug(LogCategory.Connection, $"Cleared {pendingAcks.Count} pending ACKs");
            }
        }
        
        #endregion

        #region Auth Management
        
        private void StartAuthTimeout()
        {
            CancelAuthTimeout();
            _authTimeoutCts = new CancellationTokenSource();
            var token = _authTimeoutCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_authTimeout, token);
                    if (token.IsCancellationRequested || !string.IsNullOrEmpty(SessionKey))
                    {
                        return;
                    }

                    Logger.Warning(LogCategory.Connection, "Auth timeout: LOGI not received within 10 seconds");
                    
                    // Handle timeout
                    var error = new GimException(GimErrorCode.LoginTimeout, "Authentication timeout (LOGI not received).");
                    
                    if (_loginHandler != null)
                    {
                        _loginHandler.Invoke(null, error);
                        _loginHandler = null;
                    }
                    
                    // Disconnect if auth failed
                    _webSocketClient?.Disconnect();
                    
                }
                catch (TaskCanceledException)
                {
                    // ignore
                }
            }, token);
        }

        private void CancelAuthTimeout()
        {
            if (_authTimeoutCts != null)
            {
                _authTimeoutCts.Cancel();
                _authTimeoutCts.Dispose();
                _authTimeoutCts = null;
            }
        }
        
        #endregion

        #region Reconnection Management

        /// <summary>
        /// Attempt to reconnect using stored connection parameters
        /// </summary>
        private void AttemptReconnection()
        {
            if (_lastConfig == null)
            {
                Logger.Warning(LogCategory.Connection, "Cannot reconnect: missing connection config");
                return;
            }

            // Check if we should attempt reconnection
            var error = new GimException(GimErrorCode.WebSocketConnectionClosed, "Connection lost");
            if (!_reconnectionManager.ShouldAttemptReconnect(error))
            {
                Logger.Warning(LogCategory.Connection, "Reconnection blocked by ReconnectionManager");
                return;
            }

            // Cancel any previous reconnection task
            CancelReconnection();

            // Get retry delay
            float delaySeconds = _reconnectionManager.GetNextRetryDelay();
            Logger.Info(LogCategory.Connection, $"Reconnecting in {delaySeconds} seconds (attempt #{_reconnectionManager.CurrentAttempt})");

            // Schedule reconnection with cancellation support
            _reconnectionCts = new CancellationTokenSource();
            var token = _reconnectionCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    if (delaySeconds > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                    }

                    if (token.IsCancellationRequested || _isDisposed)
                        return;

                    // Check if still disconnected
                    if (!IsConnected && !_isIntentionalDisconnect)
                    {
                        Logger.Info(LogCategory.Connection, "Executing reconnection attempt");
                        _webSocketClient.Connect(_lastConfig);
                    }
                }
                catch (TaskCanceledException)
                {
                    Logger.Debug(LogCategory.Connection, "Reconnection cancelled");
                }
                catch (Exception ex)
                {
                    Logger.Error(LogCategory.Connection, "Reconnection scheduling error", ex);
                }
            }, token);
        }

        private void CancelReconnection()
        {
            if (_reconnectionCts != null)
            {
                _reconnectionCts.Cancel();
                _reconnectionCts.Dispose();
                _reconnectionCts = null;
            }
        }

        /// <summary>
        /// Check connection status and reconnect if needed
        /// Called by GIMChatMain when app returns to foreground
        /// </summary>
        public void CheckAndReconnect()
        {
            if (!IsConnected && _lastConfig != null)
            {
                Logger.Info(LogCategory.Connection, "App resumed, reconnecting");

                // Reset flags to allow reconnection after background disconnect
                _isIntentionalDisconnect = false;
                _reconnectionManager.OnConnectionSuccess(); // Reset retry counter

                // Set reconnecting flag so OnReconnectSucceeded fires after successful connection
                _isReconnecting = true;
                OnReconnectStarted?.Invoke();

                AttemptReconnection();
            }
        }

        #endregion

        #region Helpers
        
        private static string ExtractReqId(string payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                return null;
            }

            const string key = "\"req_id\":\"";
            var start = payload.IndexOf(key, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            start += key.Length;
            var end = payload.IndexOf('"', start);
            if (end < 0 || end <= start)
            {
                return null;
            }

            return payload.Substring(start, end - start);
        }
        
        #endregion

        #region Cleanup

        /// <summary>
        /// Dispose and cleanup resources
        /// </summary>
        public void Dispose()
        {
            _isDisposed = true;
            CancelAuthTimeout();
            CancelReconnection();
            
            // Unregister WebSocket event handlers
            if (_webSocketClient != null)
            {
                _webSocketClient.OnConnected -= HandleWebSocketConnected;
                _webSocketClient.OnDisconnected -= HandleWebSocketDisconnected;
                _webSocketClient.OnError -= HandleWebSocketError;
                _webSocketClient.OnMessageReceived -= HandleWebSocketMessageReceived;
            }

            // Unregister TokenRefreshManager event handlers
            if (_tokenRefreshManager != null)
            {
                _tokenRefreshManager.OnTokenRefreshRequired -= ForwardTokenRefreshRequired;
                _tokenRefreshManager.OnSessionRefreshed -= ForwardSessionRefreshed;
                _tokenRefreshManager.OnSessionDidHaveError -= ForwardSessionDidHaveError;
                _tokenRefreshManager.OnNewTokenReceived -= HandleNewTokenReceived;
            }

            // Clear event processor
            _eventProcessor?.ClearHandlers();

            // Clear pending ACKs
            ClearAllPendingAcks();

            Logger.Info(LogCategory.Connection, "ConnectionManager disposed");
        }

        #endregion
    }

    /// <summary>
    /// Pending ACK tracking
    /// </summary>
    internal class PendingAck
    {
        public string RequestId { get; set; }
        public TaskCompletionSource<string> TaskCompletionSource { get; set; }
        public CancellationTokenSource TimeoutCancellation { get; set; }
    }
}
