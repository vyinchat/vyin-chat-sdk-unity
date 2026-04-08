// -----------------------------------------------------------------------------
//
// WebSocket Configuration
// Based on Swift SDK GIMConnection.getSocketPath()
//
// -----------------------------------------------------------------------------

using System;
using Gamania.GIMChat;

namespace Gamania.GIMChat.Internal.Data.Network
{
    /// <summary>
    /// WebSocket connection configuration
    /// </summary>
    public class WebSocketConfig
    {
        /// <summary>
        /// Application ID (e.g., "adb53e88-4c35-469a-a888-9e49ef1641b2")
        /// </summary>
        public string ApplicationId { get; set; }

        /// <summary>
        /// User ID for authentication
        /// </summary>
        public string UserId { get; set; }

        /// <summary>
        /// Access token for authentication
        /// </summary>
        public string AccessToken { get; set; }

        /// <summary>
        /// Environment domain (default: "gamania.chat")
        /// Only used when CustomWebSocketBaseUrl is null
        /// </summary>
        public string EnvironmentDomain { get; set; } = "gamania.chat";

        /// <summary>
        /// Custom WebSocket base URL (e.g., "wss://custom-app.dev.gim.beango.com")
        /// If set, this URL will be used directly instead of building from ApplicationId and EnvironmentDomain
        /// </summary>
        public string CustomWebSocketBaseUrl { get; set; }

        /// <summary>
        /// App version (optional)
        /// </summary>
        public string AppVersion { get; set; }

        /// <summary>
        /// SDK version
        /// </summary>
        public string SdkVersion { get; set; } = GimSdkInfo.Version;

        /// <summary>
        /// SDK module (e.g., "Chat")
        /// </summary>
        public string SdkModule { get; set; } = GimSdkInfo.Module;

        /// <summary>
        /// API version for error code compatibility
        /// </summary>
        public string ApiVersion { get; set; } = ApiVersionConfig.DefaultVersion;

        /// <summary>
        /// Platform version (e.g., Unity version)
        /// If not set, will use "Unknown"
        /// </summary>
        public string PlatformVersion { get; set; }

        /// <summary>
        /// Connection timeout in seconds (default: 10)
        /// </summary>
        public float ConnectionTimeout { get; set; } = 10f;

        /// <summary>
        /// Build WebSocket URL
        /// If CustomWebSocketBaseUrl is set, uses it directly
        /// Otherwise builds: wss://{appId}.{domain}/ws?user_id=xxx&access_token=yyy&...
        /// Based on Swift SDK: GIMConnection.getSocketPath()
        /// </summary>
        public string BuildWebSocketUrl()
        {
            if (string.IsNullOrEmpty(UserId))
            {
                throw new ArgumentException("UserId cannot be null or empty");
            }

            // Determine base URL
            string baseUrl;
            if (!string.IsNullOrEmpty(CustomWebSocketBaseUrl))
            {
                // Use custom URL directly
                baseUrl = CustomWebSocketBaseUrl.TrimEnd('/');
            }
            else
            {
                // Build from ApplicationId and EnvironmentDomain
                if (string.IsNullOrEmpty(ApplicationId))
                {
                    throw new ArgumentException("ApplicationId cannot be null or empty when CustomWebSocketBaseUrl is not set");
                }
                baseUrl = $"wss://{ApplicationId}.{EnvironmentDomain}";
            }

            // Add /ws path if not already present
            string host = baseUrl.EndsWith("/ws") ? baseUrl : $"{baseUrl}/ws";

            // Build query parameters
            var queryParams = new System.Collections.Generic.List<string>
            {
                $"p=Unity",  // Platform
                $"pv={Uri.EscapeDataString(PlatformVersion ?? "Unknown")}",  // Platform version
                $"sv={Uri.EscapeDataString(SdkVersion)}",  // SDK version
                $"m={Uri.EscapeDataString(SdkModule)}",  // SDK module
                $"ai={Uri.EscapeDataString(ApplicationId)}",  // Application ID
                $"user_id={Uri.EscapeDataString(UserId)}"  // User ID
            };

            // Add optional access token
            if (!string.IsNullOrEmpty(AccessToken))
            {
                queryParams.Add($"access_token={Uri.EscapeDataString(AccessToken)}");
            }

            // Add optional app version
            if (!string.IsNullOrEmpty(AppVersion))
            {
                queryParams.Add($"av={Uri.EscapeDataString(AppVersion)}");
            }

            if (!string.IsNullOrEmpty(ApiVersion))
            {
                queryParams.Add($"{ApiVersionConfig.QueryParamName}={Uri.EscapeDataString(ApiVersion)}");
            }

            // Combine host + query string
            return $"{host}?{string.Join("&", queryParams)}";
        }
    }
}
