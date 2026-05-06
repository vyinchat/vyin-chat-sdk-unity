using System;
using System.Text.RegularExpressions;
using Gamania.GIMChat;
using Gamania.GIMChat.Internal.Domain.Log;

namespace Gamania.GIMChat.Internal.Platform.Unity
{
    /// <summary>
    /// Unity-specific logger implementation
    /// </summary>
    internal partial class UnityLoggerImpl : ILogger
    {
        private static UnityLoggerImpl _instance;
        private static readonly object _lock = new();

        public static UnityLoggerImpl Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new UnityLoggerImpl();
                    }
                }
                return _instance;
            }
        }

        private LogLevel _currentLevel;

        private UnityLoggerImpl()
        {
            _currentLevel = LogLevel.Info;
        }

        #region ILogger Implementation - String Tag

        public void Verbose(string tag, string message)
        {
            if (_currentLevel > LogLevel.Verbose)
                return;

            Log(LogLevel.Verbose, tag, message);
        }

        public void Debug(string tag, string message)
        {
            if (_currentLevel > LogLevel.Debug)
                return;

            Log(LogLevel.Debug, tag, message);
        }

        public void Info(string tag, string message)
        {
            if (_currentLevel > LogLevel.Info)
                return;

            Log(LogLevel.Info, tag, message);
        }

        public void Warning(string tag, string message)
        {
            if (_currentLevel > LogLevel.Warning)
                return;

            Log(LogLevel.Warning, tag, message);
        }

        public void Error(string tag, string message, Exception exception = null)
        {
            if (_currentLevel > LogLevel.Error)
                return;

            Log(LogLevel.Error, tag, message, exception);
        }

        #endregion

        #region ILogger Implementation - LogCategory

        public void Verbose(LogCategory category, string message)
        {
            Verbose(category.ToString(), message);
        }

        public void Debug(LogCategory category, string message)
        {
            Debug(category.ToString(), message);
        }

        public void Info(LogCategory category, string message)
        {
            Info(category.ToString(), message);
        }

        public void Warning(LogCategory category, string message)
        {
            Warning(category.ToString(), message);
        }

        public void Error(LogCategory category, string message, Exception exception = null)
        {
            Error(category.ToString(), message, exception);
        }

        #endregion

        #region Configuration

        public void SetLogLevel(LogLevel level)
        {
            _currentLevel = level;
        }

        public LogLevel GetLogLevel()
        {
            return _currentLevel;
        }

        #endregion

        #region Private Methods

        private void Log(LogLevel level, string tag, string message, Exception exception = null)
        {
            // Redact PII
            message = RedactPII(message);

            // Format message
            var formattedMessage = $"[{tag}] {message}";
            if (exception is GimException vcEx)
            {
                formattedMessage += $" | GimException({vcEx.Code}): {vcEx.Message}";
            }
            else if (exception != null)
            {
                formattedMessage += $" | {exception}";
            }

            // UnityEngine.Debug.Log is thread-safe, so log directly
            // without going through MainThreadDispatcher (which requires main thread)
            switch (level)
            {
                case LogLevel.Warning:
                    UnityEngine.Debug.LogWarning(formattedMessage);
                    break;
                case LogLevel.Error:
                    UnityEngine.Debug.LogError(formattedMessage);
                    break;
                default: // Verbose, Debug, Info
                    UnityEngine.Debug.Log(formattedMessage);
                    break;
            }
        }

        private static string RedactPII(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message ?? string.Empty;

            // Redact token, session_key
            message = TokenRegex.Replace(message, "$1: ***");

            // Redact user_id
            message = UserIdRegex.Replace(message, "user_id: ***");

            // Redact email
            message = EmailRegex.Replace(message, match =>
                {
                    var parts = match.Value.Split('@');
                    if (parts.Length != 2) return match.Value;

                    var domainParts = parts[1].Split('.');
                    if (domainParts.Length < 2) return match.Value;

                    return $"{parts[0][0]}***@{domainParts[0][0]}***.{domainParts[domainParts.Length - 1]}";
                });

            return message;
        }

        #endregion

        #region Testing Support

        /// <summary>
        /// Reset singleton for testing purposes only
        /// </summary>
        internal static void ResetForTesting()
        {
            lock (_lock)
            {
                _instance = null;
            }
        }

        private static readonly Regex TokenRegex = new(
            @"(token|session_key)[:=]\s*[\w\-_]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex UserIdRegex = new(
            @"user_id[:=]\s*\d+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        private static readonly Regex EmailRegex = new(
            @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b",
            RegexOptions.Compiled
        );

        #endregion
    }
}
