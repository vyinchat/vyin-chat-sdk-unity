// -----------------------------------------------------------------------------
// GIMChat SDK - Public API Entry Point
// -----------------------------------------------------------------------------

using System;
using Gamania.GIMChat.Internal;
using Gamania.GIMChat.Internal.Platform.Unity;
using Logger = Gamania.GIMChat.Internal.Domain.Log.Logger;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Main entry point for the GIMChat SDK.
    /// Provides static methods for initialization, connection, and messaging.
    /// </summary>
    public static class GIMChat
    {
        private static readonly GIMChatMain _impl;

        static GIMChat()
        {
            Logger.SetInstance(UnityLoggerImpl.Instance);
            _impl = GIMChatMain.Instance;
        }

        #region Properties

        public static bool IsInitialized => _impl.IsInitialized;
        public static bool UseLocalCaching => _impl.UseLocalCaching;
        public static string GetApplicationId() => _impl.AppId;
        public static GimLogLevel GetLogLevel() => _impl.LogLevel;
        public static string GetAppVersion() => _impl.AppVersion;

        /// <summary>
        /// Get current WebSocket connection state.
        /// </summary>
        public static GimConnectionState GetConnectionState() => _impl.GetConnectionState();

        /// <summary>
        /// Get current connected user.
        /// </summary>
        public static GimUser CurrentUser => _impl.GetCurrentUser();

        #endregion

        #region Initialization

        /// <summary>
        /// Initializes the GIMChat SDK with the specified parameters.
        /// This method must be called before any other SDK operations.
        /// </summary>
        /// <param name="initParams">Initialization parameters including AppId and optional settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when initParams is null.</exception>
        /// <exception cref="ArgumentException">Thrown when AppId is empty or mismatched.</exception>
        /// <example>
        /// <code>
        /// var initParams = new GimInitParams("your-app-id", logLevel: GimLogLevel.Debug);
        /// GIMChat.Init(initParams);
        /// </code>
        /// </example>
        public static void Init(GimInitParams initParams)
        {
            _impl.Init(initParams);

            var logLevel = Internal.Domain.Log.LogLevelMapper.FromVcLogLevel(initParams.LogLevel);
            Logger.SetLogLevel(logLevel);
        }

        #endregion

        #region Connection

        /// <summary>
        /// Connects to the GIMChat server with user ID only.
        /// Uses default hosts derived from the AppId.
        /// </summary>
        /// <param name="userId">The user ID to connect with.</param>
        /// <param name="callback">Callback invoked with the connected user or error message.</param>
        public static void Connect(string userId, GimUserHandler callback)
        {
            Connect(userId, null, null, null, callback);
        }

        /// <summary>
        /// Connects to the GIMChat server with user ID and auth token.
        /// Uses default hosts derived from the AppId.
        /// </summary>
        /// <param name="userId">The user ID to connect with.</param>
        /// <param name="authToken">Optional authentication token (pass null if not required).</param>
        /// <param name="callback">Callback invoked with the connected user or error message.</param>
        public static void Connect(string userId, string authToken, GimUserHandler callback)
        {
            Connect(userId, authToken, null, null, callback);
        }

        /// <summary>
        /// Connects to the GIMChat server with full configuration.
        /// </summary>
        /// <param name="userId">The user ID to connect with.</param>
        /// <param name="authToken">Optional authentication token (pass null if not required).</param>
        /// <param name="apiHost">Custom API host URL (e.g., "https://api.example.com"), or null for default.</param>
        /// <param name="wsHost">Custom WebSocket host URL (e.g., "wss://ws.example.com"), or null for default.</param>
        /// <param name="callback">Callback invoked with the connected user or error message.</param>
        /// <exception cref="InvalidOperationException">Thrown when SDK is not initialized.</exception>
        public static void Connect(string userId, string authToken, string apiHost, string wsHost, GimUserHandler callback)
        {
            _impl.Connect(userId, authToken, apiHost, wsHost, callback);
        }

        #endregion

        #region Background Disconnection Configuration

        /// <summary>
        /// Sets background disconnection configuration. Call before Connect().
        /// Controls how the SDK behaves when the app enters background.
        /// </summary>
        public static void SetBackgroundDisconnectionConfig(GimBackgroundDisconnectionConfig config)
        {
            _impl.SetBackgroundDisconnectionConfig(config ?? GimBackgroundDisconnectionConfig.Default);
        }

        /// <summary>
        /// Enable/disable automatic reconnection on network change.
        /// </summary>
        public static void SetNetworkAwarenessReconnection(bool isOn)
        {
            _impl.SetNetworkAwarenessReconnection(isOn);
        }

        /// <summary>
        /// Enable/disable application lifecycle tracking.
        /// </summary>
        public static void SetTrackingApplicationState(bool isOn)
        {
            _impl.SetTrackingApplicationState(isOn);
        }

        /// <summary>
        /// Get current background disconnection configuration.
        /// </summary>
        public static GimBackgroundDisconnectionConfig GetBackgroundDisconnectionConfig()
        {
            return _impl.GetBackgroundDisconnectionConfig();
        }

        #endregion

        /// <summary>
        /// Sets the session handler for token refresh callbacks. Pass null to clear.
        /// </summary>
        /// <param name="handler">Session handler implementation.</param>
        public static void SetSessionHandler(IGimSessionHandler handler)
        {
            _impl.SetSessionHandler(handler);
        }

        /// <summary>
        /// Enable or disable automatic message resending on reconnection.
        /// When enabled, failed messages due to connection issues will be automatically
        /// resent when the connection is restored.
        /// Default: false (opt-in feature)
        /// </summary>
        /// <param name="enable">True to enable auto-resend, false to disable.</param>
        public static void SetEnableMessageAutoResend(bool enable)
        {
            _impl.SetEnableMessageAutoResend(enable);
        }

        #region Connection Handler

        /// <summary>
        /// Registers a connection handler to receive connection state events.
        /// </summary>
        /// <param name="id">Unique identifier for the handler.</param>
        /// <param name="handler">Handler instance with event callbacks.</param>
        public static void AddConnectionHandler(string id, GimConnectionHandler handler)
        {
            _impl.AddConnectionHandler(id, handler);
        }

        /// <summary>
        /// Removes a previously registered connection handler.
        /// </summary>
        /// <param name="id">Identifier of the handler to remove.</param>
        public static void RemoveConnectionHandler(string id)
        {
            _impl.RemoveConnectionHandler(id);
        }

        #endregion

        #region Group Channel Collection

        /// <summary>
        /// Creates a new query for fetching a paginated list of group channels.
        /// </summary>
        /// <example>
        /// var collection = GIMChat.CreateGroupChannelCollection(limit: 20);
        /// collection.LoadMore((channels, error) => { ... });
        /// </example>
        /// <param name="limit">Max number of channels to load per page (default 20).</param>
        /// <returns>A new <see cref="GimGroupChannelCollection"/> instance.</returns>
        public static GimGroupChannelCollection CreateGroupChannelCollection(int limit = 20)
        {
            EnsureInitialized();
            var query = GimGroupChannelListQuery.Create(limit);
            return new GimGroupChannelCollection(query);
        }

        /// <summary>
        /// Creates a new collection for fetching a paginated list of messages in a specific channel.
        /// </summary>
        /// <param name="channel">The channel to load messages for.</param>
        /// <param name="startingPoint">The starting timestamp (Unix ms) to load messages from. Defaults to long.MaxValue (latest).</param>
        /// <returns>A new <see cref="GimMessageCollection"/> instance.</returns>
        public static GimMessageCollection CreateMessageCollection(GimGroupChannel channel, long startingPoint = long.MaxValue)
        {
            EnsureInitialized();
            return new GimMessageCollection(channel, startingPoint);
        }

        /// <summary>
        /// Creates a new collection for fetching a paginated list of messages in a specific channel
        /// with advanced configuration options.
        /// </summary>
        /// <param name="channel">The channel to load messages for.</param>
        /// <param name="createParams">The creation parameters including starting point and message list params.</param>
        /// <returns>A new <see cref="GimMessageCollection"/> instance.</returns>
        public static GimMessageCollection CreateMessageCollection(GimGroupChannel channel, GimMessageCollectionCreateParams createParams)
        {
            EnsureInitialized();
            return new GimMessageCollection(channel, createParams);
        }

        private static void EnsureInitialized()
        {
            if (!IsInitialized)
            {
                throw new InvalidOperationException("GIMChat SDK is not initialized. Call GIMChat.Init() first.");
            }
        }

        #endregion

        #region User Event Handler

        /// <summary>
        /// Registers a user event handler to receive user-related events.
        /// </summary>
        /// <param name="identifier">Unique identifier for the handler.</param>
        /// <param name="handler">Handler instance with event callbacks.</param>
        public static void AddUserEventHandler(string identifier, GimUserEventHandler handler)
        {
            GimSdkDelegateManager.Instance.AddUserEventHandler(identifier, handler);
        }

        /// <summary>
        /// Removes a previously registered user event handler.
        /// </summary>
        /// <param name="identifier">Identifier of the handler to remove.</param>
        /// <returns>The removed handler, or null if not found.</returns>
        public static GimUserEventHandler RemoveUserEventHandler(string identifier)
        {
            return GimSdkDelegateManager.Instance.RemoveUserEventHandler(identifier);
        }

        /// <summary>
        /// Removes all registered user event handlers.
        /// </summary>
        public static void RemoveAllUserEventHandlers()
        {
            GimSdkDelegateManager.Instance.RemoveAllUserEventHandlers();
        }

        #endregion

        #region User Profile

        /// <summary>
        /// Updates the current user's profile information.
        /// </summary>
        /// <param name="updateParams">Update parameters.</param>
        /// <param name="handler">Completion handler with error (null on success).</param>
        public static void UpdateCurrentUserInfo(GimUserUpdateParams updateParams, GimErrorHandler handler)
        {
            _impl.UpdateCurrentUserInfo(updateParams, handler);
        }

        /// <summary>
        /// Updates the current user's profile information (async version).
        /// </summary>
        /// <param name="updateParams">Update parameters.</param>
        /// <returns>Task that completes when update succeeds.</returns>
        /// <exception cref="GimException">Thrown on failure.</exception>
        public static System.Threading.Tasks.Task UpdateCurrentUserInfoAsync(GimUserUpdateParams updateParams)
        {
            return _impl.UpdateCurrentUserInfoAsync(updateParams);
        }

        #endregion

        #region Testing Support

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        /// <summary>
        /// Resets all SDK state for testing purposes.
        /// WARNING: Do not use in production code.
        /// </summary>
        public static void ResetForTesting()
        {
            _impl.Reset();

            UnityLoggerImpl.ResetForTesting();
            Logger.ResetForTesting();
            Logger.SetInstance(UnityLoggerImpl.Instance);
        }
#endif

        #endregion
    }
}
 
