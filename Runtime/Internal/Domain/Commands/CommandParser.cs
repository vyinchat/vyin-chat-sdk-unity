// -----------------------------------------------------------------------------
//
// Command Parser - Domain Layer
// Parses WebSocket command messages
// Format: COMMAND{json_payload}
//
// -----------------------------------------------------------------------------

using System;
using Newtonsoft.Json;
using Logger = Gamania.GIMChat.Internal.Domain.Log.Logger;

namespace Gamania.GIMChat.Internal.Domain.Commands
{
    /// <summary>
    /// Parser for WebSocket command messages
    /// Command format: COMMAND{json_payload}
    /// Example: LOGI{"key":"session_123","error":false}
    /// </summary>
    public static class CommandParser
    {
        /// <summary>
        /// Extract command type from message
        /// </summary>
        /// <param name="message">Raw command message (e.g., "LOGI{...}")</param>
        /// <returns>Command type or null if parsing fails</returns>
        public static CommandType? ExtractCommandType(string message)
        {
            if (string.IsNullOrEmpty(message) || message.Length < 4)
            {
                return null;
            }

            // Extract first 4 characters as command type
            string commandTypeStr = message.Substring(0, 4);

            if (Enum.TryParse<CommandType>(commandTypeStr, out CommandType commandType))
            {
                return commandType;
            }

            return null;
        }

        /// <summary>
        /// Extract JSON payload from command message
        /// </summary>
        /// <param name="message">Raw command message (e.g., "LOGI{...}")</param>
        /// <returns>JSON payload string or null if no payload</returns>
        public static string ExtractPayload(string message)
        {
            if (string.IsNullOrEmpty(message) || message.Length < 5)
            {
                return null;
            }

            // Find opening brace
            int braceIndex = message.IndexOf('{');
            if (braceIndex < 0)
            {
                return null;
            }

            // Extract everything from opening brace to end
            return message.Substring(braceIndex);
        }

        /// <summary>
        /// Parse LOGI command from JSON payload
        /// </summary>
        /// <param name="payload">JSON payload (e.g., "{\"session_key\":\"...\"}")</param>
        /// <returns>Parsed LogiCommand or null if parsing fails</returns>
        public static LogiCommand ParseLogiCommand(string payload)
        {
            try
            {
                if (string.IsNullOrEmpty(payload))
                {
                    return null;
                }

                // Parse JSON
                LogiCommand logiCommand = JsonConvert.DeserializeObject<LogiCommand>(payload);
                return logiCommand;
            }
            catch (Exception ex)
            {
                Logger.Error("CommandParser", $"ParseLogiCommand exception: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }
    }
}
