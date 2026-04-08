using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Newtonsoft.Json;
using Gamania.GIMChat.Internal.Data.Network;
using Gamania.GIMChat.Internal.Data.Repositories;
using Gamania.GIMChat.Internal.Data.DTOs;
using Gamania.GIMChat.Internal.Data.Mappers;
using Gamania.GIMChat.Internal.Domain.Log;
using Gamania.GIMChat.Internal.Domain.Commands;
using Gamania.GIMChat.Internal.Domain.Mappers;
using Gamania.GIMChat.Internal.Domain.Message;
using Gamania.GIMChat.Internal.Domain.Models;
using Gamania.GIMChat.Internal.Domain.Repositories;
using Gamania.GIMChat.Internal.Domain.UseCases;
using Gamania.GIMChat.Internal.Data.Message;
using Gamania.GIMChat.Internal.Platform.Unity.Network;
using Logger = Gamania.GIMChat.Internal.Domain.Log.Logger;

namespace Gamania.GIMChat.Internal.Platform.Unity
{
    internal class GIMChatMain
    {
        private static GIMChatMain _instance;
        private IHttpClient _httpClient;
        private IWebSocketClient _webSocketClient;
        private ConnectionManager _connectionManager;
        private IChannelRepository _channelRepository;
        private IMessageRepository _messageRepository;
        private IUserRepository _userRepository;
        private IMessageAutoResender _messageAutoResender;
        private CancellationTokenSource _resendCts;
        private LifecycleCallbacks _lifecycleCallbacks;
        private string _baseUrl;
        private GimInitParams _initParams;
        private GimBackgroundDisconnectionConfig _backgroundDisconnectionConfig;

        // Background timeout tracking
        private float _backgroundStartTime;
        private bool _isInBackground;

        // Session handler for token refresh callbacks
        private IGimSessionHandler _sessionHandler;

        // Connection handlers
        private readonly Dictionary<string, GimConnectionHandler> _connectionHandlers = new();

        // Token refresh callback bridge (for testing)
        internal Action<string> OnTokenProvided;

        // Proactive refresh tracking
        private string _currentAccessToken;
        private GimUser _currentUser;
        private float _lastProactiveCheckTime;
        private const float ProactiveRefreshCheckIntervalSeconds = 60f;

        // Host configuration constants
        private const string API_HOST_PREFIX = "https://";
        private const string WS_HOST_PREFIX = "wss://";
        private const string HOST_POSTFIX = "gamania.chat";

        public static GIMChatMain Instance
        {
            get
            {
                _instance ??= new GIMChatMain();
                return _instance;
            }
        }

        public bool IsInitialized => _initParams != null;
        public string AppId => _initParams?.AppId;
        public string AppVersion => _initParams?.AppVersion;
        public GimLogLevel LogLevel => _initParams?.LogLevel ?? GimLogLevel.None;
        public bool UseLocalCaching => _initParams?.IsLocalCachingEnabled ?? false;

        public GIMChatMain()
        {
            _httpClient = new UnityHttpClient();
            _webSocketClient = new UnityWebSocketClient();
            _messageAutoResender = new MessageAutoResender();
            _backgroundDisconnectionConfig = GimBackgroundDisconnectionConfig.Default;
        }

        public void Init(GimInitParams initParams)
        {
            if (initParams == null)
            {
                throw new ArgumentNullException(nameof(initParams));
            }

            if (string.IsNullOrEmpty(initParams.AppId))
            {
                throw new ArgumentException("AppId cannot be null or empty", nameof(initParams));
            }

            if (IsInitialized && AppId != initParams.AppId)
            {
                throw new ArgumentException(
                    $"AppId must match previous initialization. Previous: {AppId}, New: {initParams.AppId}",
                    nameof(initParams));
            }

            _initParams = initParams;
            Logger.Info($"Initialized with AppId: {initParams.AppId}, LocalCaching: {initParams.IsLocalCachingEnabled}, LogLevel: {initParams.LogLevel}");
        }

        public void Connect(string userId, string authToken, string apiHost, string wsHost, GimUserHandler callback)
        {
            if (_initParams == null)
            {
                var errorMsg = "GIMChatMain instance hasn't been initialized. Try GIMChat.Init().";
                var error = new GimException(GimErrorCode.InvalidInitialization, errorMsg);
                Logger.Error(LogCategory.Connection, errorMsg, error);
                callback?.Invoke(null, error);
                return;
            }

            if (string.IsNullOrEmpty(userId))
            {
                var errorMsg = "userId is empty.";
                var error = new GimException(GimErrorCode.InvalidParameter, errorMsg);
                Logger.Error(LogCategory.Connection, errorMsg, error);
                callback?.Invoke(null, error);
                return;
            }

            ConnectInternal(userId, authToken, apiHost, wsHost, callback);
        }

        private void ConnectInternal(string userId, string authToken, string apiHost, string wsHost, GimUserHandler callback)
        {
            apiHost = string.IsNullOrWhiteSpace(apiHost) ? GetDefaultApiHost(_initParams.AppId) : apiHost;
            wsHost = string.IsNullOrWhiteSpace(wsHost) ? GetDefaultWsHost(_initParams.AppId) : wsHost;

            Logger.Info(LogCategory.Connection, $"Connecting with API host: {apiHost}, WS host: {wsHost}");

            // Initialize HTTP repositories with API host
            _baseUrl = apiHost;
            _channelRepository = new ChannelRepositoryImpl(_httpClient, _baseUrl);
            _userRepository = new UserRepositoryImpl(_httpClient, _baseUrl);
            Logger.Debug(LogCategory.Http, $"HTTP initialized with API host: {_baseUrl}");

            // Create WebSocket configuration
            var wsConfig = new WebSocketConfig
            {
                ApplicationId = _initParams.AppId,
                UserId = userId,
                AccessToken = authToken,
                AppVersion = _initParams.AppVersion,
                CustomWebSocketBaseUrl = wsHost
            };
            _currentAccessToken = authToken;

            // Initialize Connection Manager
            if (_connectionManager == null)
            {
                _connectionManager = new ConnectionManager(_webSocketClient);
                _connectionManager.OnAuthenticated += SetSessionKey;
                _connectionManager.OnAuthenticated += HandleAuthenticated;
                _connectionManager.OnBroadcastCommandReceived += HandleBroadcastCommand;
                SubscribeToTokenRefreshEvents();
                SubscribeToConnectionEvents();
            }

            // Initialize Message Repository
            _messageRepository = new MessageRepositoryImpl(_connectionManager, _httpClient, _baseUrl);

            // Setup lifecycle monitoring for reconnection
            SetupLifecycleMonitoring();

            // Start WebSocket connection via Connection Manager
            Logger.Info(LogCategory.Connection, "Starting WebSocket connection via ConnectionManager");
            
            // Convert GimUserHandler to Action<GimUser, GimException>
            Action<GimUser, GimException> connectionCallback = (user, error) =>
            {
                if (error == null)
                {
                    _currentUser = user;
                }
                callback?.Invoke(user, error);
            };
            _connectionManager.Connect(userId, authToken, wsConfig, connectionCallback);
        }

        private string GetDefaultApiHost(string appId)
        {
            return $"{API_HOST_PREFIX}{appId}.{HOST_POSTFIX}";
        }

        private string GetDefaultWsHost(string appId)
        {
            return $"{WS_HOST_PREFIX}{appId}.{HOST_POSTFIX}";
        }

        /// <summary>
        /// Set session key for authenticated HTTP requests
        /// Called after WebSocket connection establishes session
        /// </summary>
        public void SetSessionKey(string sessionKey)
        {
            if (_httpClient is UnityHttpClient unityHttpClient)
            {
                unityHttpClient.SetSessionKey(sessionKey);
                Logger.Debug(LogCategory.Http, "Session key updated");
            }
        }

        /// <summary>
        /// Handle successful authentication - notify auto-resender and trigger resend loop.
        /// </summary>
        private void HandleAuthenticated(string sessionKey)
        {
            _messageAutoResender?.OnConnected();
            StartPendingMessageResend();
        }

        /// <summary>
        /// Cancel any running resend loop and start a new one.
        /// </summary>
        private void StartPendingMessageResend()
        {
            // Cancel previous resend loop
            _resendCts?.Cancel();
            _resendCts?.Dispose();
            _resendCts = new CancellationTokenSource();

            var token = _resendCts.Token;
            _ = ProcessPendingMessagesAsync(token);
        }

        /// <summary>
        /// Process pending messages in the auto-resend queue.
        /// Called on reconnection to resend queued messages.
        /// </summary>
        private async Task ProcessPendingMessagesAsync(CancellationToken cancellationToken)
        {
            if (_messageAutoResender == null || !_messageAutoResender.IsEnabled)
                return;

            // Cleanup expired messages first
            _messageAutoResender.CleanupExpired();

            var useCase = new SendMessageUseCase(_messageRepository, _messageAutoResender);

            while (_messageAutoResender.TryDequeue(out var pendingMessage))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _messageAutoResender.Register(pendingMessage);
                    Logger.Info(LogCategory.Message, "[AutoResend] Resend loop cancelled");
                    break;
                }

                if (!IsConnected())
                {
                    // Connection lost during resend - re-register and stop
                    _messageAutoResender.Register(pendingMessage);
                    Logger.Info(LogCategory.Message, "[AutoResend] Connection lost, stopping resend loop");
                    break;
                }

                // Subscribe to status changes for UI notification
                pendingMessage.OnStatusChanged = HandleMessageStatusChanged;

                try
                {
                    // Apply backoff delay
                    if (pendingMessage.RetryCount > 0)
                    {
                        var delay = pendingMessage.GetBackoffDelayMs();
                        Logger.Debug(LogCategory.Message, $"[AutoResend] Waiting {delay}ms before resend");
                        await Task.Delay(delay, cancellationToken);
                    }

                    var message = await useCase.ResendAsync(pendingMessage);
                    pendingMessage.OnSuccess?.Invoke(message);
                    Logger.Info(LogCategory.Message, $"[AutoResend] Resent successfully: {pendingMessage.RequestId}");
                }
                catch (TaskCanceledException)
                {
                    _messageAutoResender.Register(pendingMessage);
                    Logger.Info(LogCategory.Message, "[AutoResend] Resend loop cancelled during delay");
                    break;
                }
                catch (GimException vcEx)
                {
                    Logger.Warning(LogCategory.Message, $"[AutoResend] Resend failed: {pendingMessage.RequestId}, error: {vcEx.ErrorCode}");
                }
                catch (Exception ex)
                {
                    Logger.Error(LogCategory.Message, $"[AutoResend] Unexpected error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Handle message status changes and notify UI via OnMessageUpdated.
        /// Called automatically when PendingMessage status transitions.
        /// </summary>
        private void HandleMessageStatusChanged(string channelUrl, GimBaseMessage message, GimSendingStatus status)
        {
            if (string.IsNullOrEmpty(channelUrl) || message == null)
                return;

            var channel = new GimGroupChannel { ChannelUrl = channelUrl };
            GimGroupChannel.TriggerMessageUpdated(channel, message);
        }

        /// <summary>
        /// Get Channel Repository instance
        /// </summary>
        public IChannelRepository GetChannelRepository()
        {
            EnsureInitialized();
            return _channelRepository;
        }

        /// <summary>
        /// Get Message Repository instance
        /// </summary>
        public IMessageRepository GetMessageRepository()
        {
            EnsureInitialized();
            return _messageRepository;
        }

        /// <summary>
        /// Get WebSocket Client instance
        /// </summary>
        public IWebSocketClient GetWebSocketClient()
        {
            EnsureInitialized();
            return _webSocketClient;
        }

        /// <summary>
        /// Get Connection Manager instance
        /// </summary>
        public ConnectionManager GetConnectionManager()
        {
            EnsureInitialized();
            return _connectionManager;
        }

        /// <summary>
        /// Check if connected to server (has valid session key)
        /// </summary>
        public bool IsConnected()
        {
            return _connectionManager != null && _connectionManager.IsConnected;
        }

        /// <summary>
        /// Get current WebSocket connection state
        /// </summary>
        public GimConnectionState GetConnectionState()
        {
            return _connectionManager?.State ?? GimConnectionState.Closed;
        }

        private void EnsureInitialized()
        {
            if (_initParams == null)
            {
                throw new GimException(
                    GimErrorCode.InvalidInitialization,
                    "GIMChat SDK is not initialized. Call GIMChat.Init() first.");
            }
        }

        /// <summary>
        /// Set background disconnection configuration.
        /// Must be called before Connect() to take effect.
        /// </summary>
        public void SetBackgroundDisconnectionConfig(GimBackgroundDisconnectionConfig config)
        {
            _backgroundDisconnectionConfig = config ?? GimBackgroundDisconnectionConfig.Default;
            Logger.Debug(LogCategory.Connection,
                $"Background disconnection config updated: TrackingAppState={config.IsTrackingApplicationState}, " +
                $"DisconnectOnBg={config.DisconnectOnBackground}, " +
                $"BgDelay={config.BackgroundDisconnectDelaySeconds}s");
        }

        /// <summary>
        /// Set network awareness reconnection
        /// </summary>
        public void SetNetworkAwarenessReconnection(bool isOn)
        {
            _backgroundDisconnectionConfig.NetworkAwarenessReconnection = isOn;
            Logger.Debug(LogCategory.Connection, $"Network awareness reconnection: {isOn}");
        }

        /// <summary>
        /// Set tracking application state
        /// </summary>
        public void SetTrackingApplicationState(bool isOn)
        {
            _backgroundDisconnectionConfig.IsTrackingApplicationState = isOn;
            Logger.Debug(LogCategory.Connection, $"Application state tracking: {isOn}");
        }

        /// <summary>
        /// Get current background disconnection configuration
        /// </summary>
        public GimBackgroundDisconnectionConfig GetBackgroundDisconnectionConfig()
        {
            return _backgroundDisconnectionConfig;
        }

        /// <summary>
        /// Enable or disable automatic message resending on reconnection.
        /// When disabled, clears the pending message queue.
        /// </summary>
        public void SetEnableMessageAutoResend(bool enable)
        {
            _messageAutoResender?.SetEnabled(enable);
            Logger.Info(LogCategory.Message, $"Message auto-resend: {(enable ? "enabled" : "disabled")}");
        }

        /// <summary>
        /// Get the message auto-resender instance.
        /// </summary>
        internal IMessageAutoResender GetMessageAutoResender()
        {
            return _messageAutoResender;
        }

        #region Connection Handler Management

        public void AddConnectionHandler(string id, GimConnectionHandler handler)
        {
            if (string.IsNullOrEmpty(id) || handler == null) return;
            _connectionHandlers[id] = handler;
        }

        public void RemoveConnectionHandler(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            _connectionHandlers.Remove(id);
        }

        private void NotifyConnectionHandlers(Action<GimConnectionHandler> action)
        {
            foreach (var handler in _connectionHandlers.Values)
            {
                try
                {
                    action(handler);
                }
                catch (Exception ex)
                {
                    Logger.Error(LogCategory.Connection, $"Connection handler error: {ex.Message}");
                }
            }
        }

        private void SubscribeToConnectionEvents()
        {
            _connectionManager.OnConnectedEvent += HandleConnectionConnected;
            _connectionManager.OnDisconnectedEvent += HandleConnectionDisconnected;
            _connectionManager.OnReconnectStarted += HandleConnectionReconnectStarted;
            _connectionManager.OnReconnectSucceeded += HandleConnectionReconnectSucceeded;
            _connectionManager.OnReconnectFailed += HandleConnectionReconnectFailed;
        }

        private void UnsubscribeFromConnectionEvents()
        {
            if (_connectionManager == null) return;

            _connectionManager.OnConnectedEvent -= HandleConnectionConnected;
            _connectionManager.OnDisconnectedEvent -= HandleConnectionDisconnected;
            _connectionManager.OnReconnectStarted -= HandleConnectionReconnectStarted;
            _connectionManager.OnReconnectSucceeded -= HandleConnectionReconnectSucceeded;
            _connectionManager.OnReconnectFailed -= HandleConnectionReconnectFailed;
        }

        private void HandleConnectionConnected(string userId)
            => NotifyConnectionHandlers(h => h.OnConnected?.Invoke(userId));

        private void HandleConnectionDisconnected(string userId)
            => NotifyConnectionHandlers(h => h.OnDisconnected?.Invoke(userId));

        private void HandleConnectionReconnectStarted()
            => NotifyConnectionHandlers(h => h.OnReconnectStarted?.Invoke());

        private void HandleConnectionReconnectSucceeded()
            => NotifyConnectionHandlers(h => h.OnReconnectSucceeded?.Invoke());

        private void HandleConnectionReconnectFailed()
            => NotifyConnectionHandlers(h => h.OnReconnectFailed?.Invoke());

        #endregion

        #region Broadcast Command Handling

        /// <summary>
        /// Handle broadcast commands from ConnectionManager
        /// </summary>
        private void HandleBroadcastCommand(CommandType type, string payload)
        {
            switch (type)
            {
                case CommandType.MESG:
                    HandleMessageReceived(payload);
                    break;
                case CommandType.MEDI:
                    HandleMessageUpdated(payload);
                    break;
                case CommandType.EROR:
                    HandleError(payload);
                    break;
                default:
                    Logger.Debug(LogCategory.WebSocket, $"Unhandled broadcast command: {type}");
                    break;
            }
        }

        /// <summary>
        /// Handle received message (MESG command) from broadcast
        /// </summary>
        private void HandleMessageReceived(string payload)
        {
            var message = ParseBroadcastMessage(payload, "MESG");
            if (message != null)
            {
                // Check if sender info has changed compared to cache
                // For current user: syncs profile silently; for others: triggers event if changed
                GimSdkDelegateManager.Instance.CheckAndNotifySenderInfoChanged(
                    message.Sender, _currentUser);

                var channel = new GimGroupChannel { ChannelUrl = message.ChannelUrl };
                GimGroupChannel.TriggerMessageReceived(channel, message);
            }
        }

        /// <summary>
        /// Handle message updated (MEDI command) from broadcast
        /// Used for streaming messages (e.g., AI responses) and message edits.
        /// </summary>
        private void HandleMessageUpdated(string payload)
        {
            var message = ParseBroadcastMessage(payload, "MEDI");
            if (message != null)
            {
                var channel = new GimGroupChannel { ChannelUrl = message.ChannelUrl };
                GimGroupChannel.TriggerMessageUpdated(channel, message);
            }
        }

        /// <summary>
        /// Parse broadcast message from JSON payload
        /// Returns null if parsing fails (passive event, can be skipped)
        /// </summary>
        private GimBaseMessage ParseBroadcastMessage(string payload, string commandType)
        {
            try
            {
                // Step 1: Parse JSON to DTO
                var dto = JsonConvert.DeserializeObject<MessageDTO>(payload);
                if (dto == null)
                {
                    Logger.Warning(LogCategory.WebSocket, $"Failed to deserialize {commandType} payload");
                    return null;
                }

                // Step 2: DTO → BO
                var bo = MessageDtoMapper.ToBusinessObject(dto);
                if (bo == null)
                {
                    Logger.Warning(LogCategory.WebSocket, $"Failed to map {commandType} to BO");
                    return null;
                }

                // Step 3: BO → Public Model
                var message = MessageBoMapper.ToPublicModel(bo);
                if (message == null)
                {
                    Logger.Warning(LogCategory.WebSocket, $"Failed to map {commandType} to public model");
                    return null;
                }

                return message;
            }
            catch (Exception ex)
            {
                Logger.Error(LogCategory.WebSocket, $"Error parsing {commandType} command", ex);
                return null;
            }
        }

        /// <summary>
        /// Handle error command (EROR)
        /// </summary>
        private void HandleError(string payload)
        {
            try
            {
                Logger.Error(LogCategory.WebSocket, $"Received EROR command: {payload}");
                // Additional error handling can be added here if needed
            }
            catch (Exception ex)
            {
                Logger.Error(LogCategory.WebSocket, "Error handling EROR command", ex);
            }
        }

        #endregion

        #region Session Handler

        /// <summary>
        /// Sets the session handler for token refresh callbacks. Pass null to clear.
        /// </summary>
        public void SetSessionHandler(IGimSessionHandler handler)
        {
            _sessionHandler = handler;
            Logger.Debug(LogCategory.Connection, handler != null 
                ? "Session handler set" 
                : "Session handler cleared");
        }

        /// <summary>
        /// Subscribe to ConnectionManager token refresh events
        /// </summary>
        private void SubscribeToTokenRefreshEvents()
        {
            if (_connectionManager == null) return;

            _connectionManager.OnTokenRefreshRequired += HandleTokenRefreshRequired;
            _connectionManager.OnSessionRefreshed += HandleSessionRefreshed;
            _connectionManager.OnSessionDidHaveError += HandleSessionDidHaveError;
        }

        /// <summary>
        /// Unsubscribe from ConnectionManager token refresh events
        /// </summary>
        private void UnsubscribeFromTokenRefreshEvents()
        {
            if (_connectionManager == null) return;

            _connectionManager.OnTokenRefreshRequired -= HandleTokenRefreshRequired;
            _connectionManager.OnSessionRefreshed -= HandleSessionRefreshed;
            _connectionManager.OnSessionDidHaveError -= HandleSessionDidHaveError;
        }

        private void HandleTokenRefreshRequired(Action<string> provideToken)
        {
            Logger.Info(LogCategory.Connection, "Token refresh required, forwarding to session handler");

            if (_sessionHandler == null)
            {
                Logger.Warning(LogCategory.Connection, "No session handler set, cannot refresh token");
                provideToken?.Invoke(null);
                return;
            }

            _sessionHandler.OnSessionTokenRequired(
                success: newToken =>
                {
                    Logger.Debug(LogCategory.Connection, "Session handler provided token");
                    _currentAccessToken = newToken;
                    OnTokenProvided?.Invoke(newToken);
                    provideToken?.Invoke(newToken);
                },
                fail: () =>
                {
                    Logger.Warning(LogCategory.Connection, "Session handler failed to provide token");
                    OnTokenProvided?.Invoke(null);
                    provideToken?.Invoke(null);
                }
            );
        }

        private void HandleSessionRefreshed()
        {
            Logger.Info(LogCategory.Connection, "Session refreshed successfully");
            _sessionHandler?.OnSessionRefreshed();
            _messageAutoResender?.OnTokenRefreshed();

            // Trigger resend of pending messages after token refresh
            StartPendingMessageResend();
        }

        private void HandleSessionDidHaveError(GimException error)
        {
            Logger.Error(LogCategory.Connection, $"Session error: {error.ErrorCode}", error);
            _sessionHandler?.OnSessionError(error);

            // Check if this is an unrecoverable error
            if (error.ErrorCode == GimErrorCode.ErrInvalidSession ||
                error.ErrorCode == GimErrorCode.ErrInvalidSessionKeyValue)
            {
                _sessionHandler?.OnSessionClosed();
            }
        }

        #region Proactive Refresh

        /// <summary>
        /// Check if token should be refreshed proactively.
        /// Should be called periodically (e.g., every 60 seconds).
        /// </summary>
        public void CheckProactiveRefresh()
        {
            // Skip if no connection manager or token
            if (_connectionManager == null || string.IsNullOrEmpty(_currentAccessToken))
            {
                return;
            }

            // Delegate to ConnectionManager which uses internal TokenRefreshManager
            _connectionManager.CheckProactiveRefresh(_currentAccessToken);
        }

        private void CheckProactiveRefreshIfNeeded()
        {
            if (_connectionManager == null || string.IsNullOrEmpty(_currentAccessToken))
            {
                return;
            }

            float currentTime = UnityEngine.Time.time;
            if (currentTime - _lastProactiveCheckTime < ProactiveRefreshCheckIntervalSeconds)
            {
                return;
            }

            _lastProactiveCheckTime = currentTime;
            _connectionManager.CheckProactiveRefresh(_currentAccessToken);
        }

        #endregion

        #region Test Helpers (Internal)

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        /// <summary>
        /// Simulate token refresh required event (for testing)
        /// </summary>
        internal void SimulateTokenRefreshRequired()
        {
            HandleTokenRefreshRequired(token => OnTokenProvided?.Invoke(token));
        }

        /// <summary>
        /// Simulate session refreshed event (for testing)
        /// </summary>
        internal void SimulateSessionRefreshed()
        {
            HandleSessionRefreshed();
        }

        /// <summary>
        /// Simulate session error event (for testing)
        /// </summary>
        internal void SimulateSessionError(GimException error)
        {
            HandleSessionDidHaveError(error);
        }

        /// <summary>
        /// Simulate session closed event (for testing)
        /// </summary>
        internal void SimulateSessionClosed()
        {
            _sessionHandler?.OnSessionClosed();
        }

        /// <summary>
        /// Set current token for testing
        /// </summary>
        internal void SetCurrentToken(string token)
        {
            _currentAccessToken = token;
        }

        /// <summary>
        /// Set channel repository for testing (e.g., MockChannelRepository)
        /// </summary>
        internal void SetChannelRepositoryForTesting(IChannelRepository repo)
        {
            _channelRepository = repo;
        }

        /// <summary>
        /// Set message repository for testing
        /// </summary>
        internal void SetMessageRepositoryForTesting(IMessageRepository repo)
        {
            _messageRepository = repo;
        }

        /// <summary>
        /// Set current user for testing (required for LoadMore to get userId)
        /// </summary>
        internal void SetCurrentUserForTesting(GimUser user)
        {
            _currentUser = user;
        }

        /// <summary>
        /// Trigger reconnect succeeded event for testing
        /// </summary>
        internal void TriggerReconnectSucceededForTesting()
        {
            HandleConnectionReconnectSucceeded();
        }

        /// <summary>
        /// Trigger channel message received event for testing
        /// </summary>
        internal void TriggerChannelMessageReceivedForTesting(string channelUrl, ChannelBO channelBo)
        {
            var channel = ChannelBoMapper.ToPublicModel(channelBo);
            GimGroupChannel.TriggerMessageReceived(channel, null);
        }

        /// <summary>
        /// Trigger channel changed event for testing
        /// </summary>
        internal void TriggerChannelChangedForTesting(string channelUrl, ChannelBO channelBo)
        {
            var channel = ChannelBoMapper.ToPublicModel(channelBo);
            GimGroupChannel.TriggerChannelChanged(channel);
        }

        /// <summary>
        /// Trigger channel deleted event for testing
        /// </summary>
        internal void TriggerChannelDeletedForTesting(string channelUrl, string deletedUrl)
        {
            GimGroupChannel.TriggerChannelDeleted(deletedUrl);
        }
#endif

        #endregion

        #endregion

        /// <summary>
        /// Reset instance state (for testing)
        /// </summary>
        public void Reset()
        {
            // Cancel pending resend loop
            _resendCts?.Cancel();
            _resendCts?.Dispose();
            _resendCts = null;

            // Unsubscribe from all connection manager events before disposing
            UnsubscribeFromConnectionEvents();
            UnsubscribeFromTokenRefreshEvents();

            // Disconnect WebSocket if connected
            if (_connectionManager != null)
            {
                _connectionManager.Disconnect();
                _connectionManager.Dispose();
                _connectionManager = null;
            }
            else if (_webSocketClient != null && _webSocketClient.IsConnected)
            {
                // Fallback direct disconnect if manager missing
                _webSocketClient.Disconnect();
            }

            // Cleanup lifecycle callbacks
            // Clear all callback references BEFORE Destroy to prevent
            // callbacks firing on disposed objects during deferred destruction
            if (_lifecycleCallbacks != null)
            {
                _lifecycleCallbacks.OnPaused = null;
                _lifecycleCallbacks.OnResumed = null;
                _lifecycleCallbacks.OnGainedFocus = null;
                _lifecycleCallbacks.OnLostFocus = null;
                _lifecycleCallbacks.OnQuitting = null;
                _lifecycleCallbacks.OnNetworkChanged = null;
                _lifecycleCallbacks.OnUpdate = null;
                GameObject.Destroy(_lifecycleCallbacks.gameObject);
                _lifecycleCallbacks = null;
            }

            // Cleanup message auto-resender
            _messageAutoResender?.Dispose();
            _messageAutoResender = new MessageAutoResender();

            _initParams = null;
            _httpClient = new UnityHttpClient();
            _webSocketClient = new UnityWebSocketClient();
            _channelRepository = null;
            _messageRepository = null;
            _userRepository = null;
            _baseUrl = null;
            _sessionHandler = null;
            OnTokenProvided = null;
            _currentAccessToken = null;
            _currentUser = null;
            _connectionHandlers.Clear();
        }

        public GimUser GetCurrentUser()
        {
            return _currentUser;
        }

        #region User Profile

        /// <summary>
        /// Updates the current user's profile information.
        /// </summary>
        public void UpdateCurrentUserInfo(GimUserUpdateParams updateParams, GimErrorHandler handler)
        {
            // Validation: Not initialized
            if (_initParams == null)
            {
                handler?.Invoke(new GimException(GimErrorCode.InvalidInitialization,
                    "GIMChat SDK is not initialized. Call GIMChat.Init() first."));
                return;
            }

            // Validation: Null params
            if (updateParams == null)
            {
                handler?.Invoke(new GimException(GimErrorCode.InvalidParameter,
                    "updateParams cannot be null."));
                return;
            }

            // Validation: All fields null
            if (!updateParams.HasAnyFieldSet())
            {
                handler?.Invoke(new GimException(GimErrorCode.InvalidParameter,
                    "At least one field must be set."));
                return;
            }

            // Validation: Not connected
            if (_currentUser == null)
            {
                handler?.Invoke(new GimException(GimErrorCode.ConnectionRequired,
                    "Not connected. Call GIMChat.Connect() first."));
                return;
            }

            // Execute update asynchronously
            _ = UpdateCurrentUserInfoInternalAsync(updateParams, handler);
        }

        private async Task UpdateCurrentUserInfoInternalAsync(GimUserUpdateParams updateParams, GimErrorHandler handler)
        {
            try
            {
                // Call user update API
                var result = await _userRepository.UpdateUserInfoAsync(
                    _currentUser.UserId,
                    updateParams.Nickname,
                    updateParams.ProfileImageUrl);

                // Update current user with result
                if (result.Nickname != null)
                {
                    _currentUser.Nickname = result.Nickname;
                }
                if (result.ProfileUrl != null)
                {
                    _currentUser.ProfileUrl = result.ProfileUrl;
                }

                // Notify user event handlers
                GimSdkDelegateManager.Instance.NotifyUserInfoUpdated(
                    new System.Collections.Generic.List<GimUser> { _currentUser });

                Logger.Info(LogCategory.Http, "User profile updated successfully");
                handler?.Invoke(null);
            }
            catch (GimException ex)
            {
                Logger.Error(LogCategory.Http, $"Failed to update user profile: {ex.ErrorCode}", ex);
                handler?.Invoke(ex);
            }
            catch (Exception ex)
            {
                Logger.Error(LogCategory.Http, $"Unexpected error updating user profile: {ex.Message}", ex);
                handler?.Invoke(new GimException(GimErrorCode.UnknownError, ex.Message));
            }
        }

        /// <summary>
        /// Updates the current user's profile information (async version).
        /// </summary>
        public Task UpdateCurrentUserInfoAsync(GimUserUpdateParams updateParams)
        {
            var tcs = new TaskCompletionSource<bool>();
            UpdateCurrentUserInfo(updateParams, error =>
            {
                if (error != null)
                {
                    tcs.SetException(error);
                }
                else
                {
                    tcs.SetResult(true);
                }
            });
            return tcs.Task;
        }

        #endregion

        #region Lifecycle Management

        /// <summary>
        /// Setup application lifecycle monitoring
        /// </summary>
        private void SetupLifecycleMonitoring()
        {
            if (_lifecycleCallbacks != null)
            {
                return; // Already setup
            }

            var go = new GameObject("GIMChatLifecycleCallbacks");
            _lifecycleCallbacks = go.AddComponent<LifecycleCallbacks>();
            GameObject.DontDestroyOnLoad(go);

            // Connect callbacks
            _lifecycleCallbacks.OnPaused = HandleApplicationPaused;
            _lifecycleCallbacks.OnResumed = HandleApplicationResumed;
            _lifecycleCallbacks.OnGainedFocus = HandleApplicationGainedFocus;
            _lifecycleCallbacks.OnLostFocus = HandleApplicationLostFocus;
            _lifecycleCallbacks.OnQuitting = HandleApplicationQuitting;
            _lifecycleCallbacks.OnNetworkChanged = HandleNetworkChanged;
            _lifecycleCallbacks.OnUpdate = HandlePeriodicChecks;
        }

        private void HandlePeriodicChecks()
        {
            // Drive connection manager update (ACK, token refresh timeout, etc.)
            _connectionManager?.Update();
            CheckBackgroundDisconnect();
            CheckProactiveRefreshIfNeeded();
        }

        private void HandleApplicationPaused()
        {
            Logger.Info(LogCategory.Connection, "Application entered background");

            if (!_backgroundDisconnectionConfig.IsTrackingApplicationState)
            {
                Logger.Debug(LogCategory.Connection, "Application state tracking disabled, ignoring pause event");
                return;
            }

            if (!_backgroundDisconnectionConfig.DisconnectOnBackground)
            {
                Logger.Debug(LogCategory.Connection, "Background disconnect disabled, keeping connection alive");
                return;
            }

            _isInBackground = true;

            // Immediate disconnect
            if (_backgroundDisconnectionConfig.BackgroundDisconnectDelaySeconds <= 0)
            {
                Logger.Info(LogCategory.Connection, "Disconnecting immediately (background mode)");
                _messageAutoResender?.OnDisconnected();
                _connectionManager?.Disconnect();
                return;
            }

            // Delayed disconnect
            _backgroundStartTime = UnityEngine.Time.time;
            Logger.Debug(LogCategory.Connection,
                $"Background disconnect scheduled in {_backgroundDisconnectionConfig.BackgroundDisconnectDelaySeconds}s");
        }

        private void HandleApplicationResumed()
        {
            Logger.Info(LogCategory.Connection, "Application returned to foreground");

            if (!_backgroundDisconnectionConfig.IsTrackingApplicationState)
            {
                Logger.Debug(LogCategory.Connection, "Application state tracking disabled, ignoring resume event");
                return;
            }

            _isInBackground = false;
            _backgroundStartTime = 0;

            // Check and reconnect if needed
            _connectionManager?.CheckAndReconnect();
        }

        private void HandleApplicationGainedFocus()
        {
            Logger.Debug(LogCategory.Connection, "Application gained focus");
        }

        private void HandleApplicationLostFocus()
        {
            Logger.Debug(LogCategory.Connection, "Application lost focus");
        }

        private void HandleApplicationQuitting()
        {
            Logger.Info(LogCategory.Connection, "Application quitting");

            // Notify auto-resender before disconnect
            _messageAutoResender?.OnDisconnected();

            // Graceful disconnect
            _connectionManager?.Disconnect();
        }

        private void HandleNetworkChanged(NetworkReachability oldStatus, NetworkReachability newStatus)
        {
            Logger.Info(LogCategory.Connection, $"Network status changed: {oldStatus} -> {newStatus}");

            // Network became reachable - attempt reconnection
            if (newStatus != NetworkReachability.NotReachable && oldStatus == NetworkReachability.NotReachable)
            {
                if (_backgroundDisconnectionConfig.NetworkAwarenessReconnection)
                {
                    Logger.Info(LogCategory.Connection, "Network became reachable, attempting reconnection");
                    _connectionManager?.CheckAndReconnect();
                }
                else
                {
                    Logger.Debug(LogCategory.Connection, "Network awareness reconnection disabled, skipping auto-reconnect");
                }
            }
        }

        /// <summary>
        /// Check if background disconnect delay has elapsed
        /// Called from LifecycleCallbacks Update loop
        /// </summary>
        private void CheckBackgroundDisconnect()
        {
            if (!_isInBackground) return;
            if (_backgroundDisconnectionConfig.BackgroundDisconnectDelaySeconds <= 0) return;
            if (_connectionManager == null || !_connectionManager.IsConnected) return;

            float elapsedTime = UnityEngine.Time.time - _backgroundStartTime;

            if (elapsedTime >= _backgroundDisconnectionConfig.BackgroundDisconnectDelaySeconds)
            {
                Logger.Info(LogCategory.Connection,
                    $"Background disconnect delay elapsed ({elapsedTime:F1}s >= {_backgroundDisconnectionConfig.BackgroundDisconnectDelaySeconds}s), disconnecting");
                _messageAutoResender?.OnDisconnected();
                _connectionManager.Disconnect();
                _isInBackground = false; // Reset flag after disconnect
            }
        }

        /// <summary>
        /// Internal MonoBehaviour to receive Unity lifecycle events and monitor network status
        /// </summary>
        private class LifecycleCallbacks : MonoBehaviour
        {
            public Action OnPaused;
            public Action OnResumed;
            public Action OnGainedFocus;
            public Action OnLostFocus;
            public Action OnQuitting;
            public Action<NetworkReachability, NetworkReachability> OnNetworkChanged;
            public Action OnUpdate;

            private NetworkReachability _lastNetworkStatus;

            void Start()
            {
                _lastNetworkStatus = Application.internetReachability;
            }

            void Update()
            {
                // Check network status changes (Platform Layer: Unity Application.internetReachability)
                CheckNetworkStatus();

                // Periodic checks (background disconnect, etc.)
                OnUpdate?.Invoke();
            }

            void OnApplicationPause(bool pauseStatus)
            {
                if (pauseStatus)
                {
                    OnPaused?.Invoke();
                }
                else
                {
                    OnResumed?.Invoke();
                }
            }

            void OnApplicationFocus(bool hasFocus)
            {
                if (hasFocus)
                {
                    OnGainedFocus?.Invoke();
                }
                else
                {
                    OnLostFocus?.Invoke();
                }
            }

            void OnApplicationQuit()
            {
                OnQuitting?.Invoke();
            }

            private void CheckNetworkStatus()
            {
                var currentStatus = Application.internetReachability;

                if (currentStatus != _lastNetworkStatus)
                {
                    var oldStatus = _lastNetworkStatus;
                    _lastNetworkStatus = currentStatus;
                    OnNetworkChanged?.Invoke(oldStatus, currentStatus);
                }
            }
        }

        #endregion
    }
}
