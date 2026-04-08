// -----------------------------------------------------------------------------
//
// LOGI Command Data Structures - Domain Layer
// Pure C# - NO Unity dependencies
//
// -----------------------------------------------------------------------------

using Newtonsoft.Json;

namespace Gamania.GIMChat.Internal.Domain.Commands
{
    /// <summary>
    /// LOGI command response from server
    /// Format: LOGI{json}
    /// </summary>
    public class LogiCommand
    {
        /// <summary>
        /// Session key for authenticated communication
        /// JSON field: "key"
        /// </summary>
        [JsonProperty("key")]
        public string SessionKey { get; set; }

        /// <summary>
        /// Error flag indicating authentication failure
        /// JSON field: "error"
        /// </summary>
        [JsonProperty("error")]
        public bool? Error { get; set; }

        /// <summary>
        /// Encryption key (if encryption is enabled)
        /// JSON field: "ekey"
        /// </summary>
        [JsonProperty("ekey")]
        public string EKey { get; set; }

        /// <summary>
        /// Ping interval in seconds (default: 15)
        /// JSON field: "ping_interval"
        /// </summary>
        [JsonProperty("ping_interval")]
        public int PingInterval { get; set; } = 15;

        /// <summary>
        /// Pong timeout in seconds (default: 5)
        /// JSON field: "pong_timeout"
        /// </summary>
        [JsonProperty("pong_timeout")]
        public int PongTimeout { get; set; } = 5;

        /// <summary>
        /// Last connected timestamp
        /// JSON field: "login_ts"
        /// </summary>
        [JsonProperty("login_ts")]
        public long LastConnected { get; set; }

        /// <summary>
        /// Unread count information
        /// JSON field: "unread_count"
        /// </summary>
        [JsonProperty("unread_count")]
        public UnreadCountModel UnreadCount { get; set; }

        // ── User Profile Fields ──────────────────────────────────────────────────

        /// <summary>
        /// User ID
        /// JSON field: "user_id"
        /// </summary>
        [JsonProperty("user_id")]
        public string UserId { get; set; }

        /// <summary>
        /// User nickname (can be "name" or "nickname" in response)
        /// JSON field: "nickname" (with alternate "name")
        /// </summary>
        [JsonProperty("nickname")]
        public string Nickname { get; set; }

        /// <summary>
        /// Alternate nickname field (some responses use "name")
        /// JSON field: "name"
        /// </summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>
        /// User profile image URL (can be "profile_url" or "image" in response)
        /// JSON field: "profile_url"
        /// </summary>
        [JsonProperty("profile_url")]
        public string ProfileUrl { get; set; }

        /// <summary>
        /// Alternate profile URL field (some responses use "image")
        /// JSON field: "image"
        /// </summary>
        [JsonProperty("image")]
        public string Image { get; set; }

        /// <summary>
        /// Gets the effective nickname (checks both "nickname" and "name" fields)
        /// </summary>
        public string GetNickname() => Nickname ?? Name;

        /// <summary>
        /// Gets the effective profile URL (checks both "profile_url" and "image" fields)
        /// </summary>
        public string GetProfileUrl() => ProfileUrl ?? Image;

        /// <summary>
        /// Check if LOGI response indicates success
        /// </summary>
        public bool IsSuccess()
        {
            return Error != true && !string.IsNullOrEmpty(SessionKey);
        }
    }

    /// <summary>
    /// Unread count model
    /// </summary>
    public class UnreadCountModel
    {
        /// <summary>
        /// Total unread count
        /// JSON field: "all"
        /// </summary>
        [JsonProperty("all")]
        public long All { get; set; }

        /// <summary>
        /// Timestamp of unread count
        /// JSON field: "ts"
        /// </summary>
        [JsonProperty("ts")]
        public long Ts { get; set; }
    }
}
