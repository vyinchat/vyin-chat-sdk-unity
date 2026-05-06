namespace Gamania.GIMChat
{
    /// <summary>
    /// Internal extension methods for GimErrorCode related to message reliability.
    /// External users should use GimBaseMessage.IsResendable property instead.
    /// </summary>
    internal static class GimErrorCodeExtensions
    {
        /// <summary>
        /// Check if error code is auto-resendable (network/connection issues).
        /// These errors will trigger automatic retry on reconnection.
        /// </summary>
        internal static bool IsAutoResendable(this GimErrorCode code)
        {
            return code switch
            {
                GimErrorCode.ConnectionRequired => true,
                GimErrorCode.WebSocketConnectionClosed => true,
                GimErrorCode.WebSocketConnectionFailed => true,
                GimErrorCode.NetworkError => true,
                GimErrorCode.RequestFailed => true,
                GimErrorCode.FileUploadTimeout => true,
                GimErrorCode.ErrFileIsNotReady => true,
                _ => false
            };
        }

        /// <summary>
        /// Check if error code is user-resendable (can be manually retried by user).
        /// Used internally by GimBaseMessage.IsResendable property.
        /// </summary>
        internal static bool IsResendable(this GimErrorCode code)
        {
            if (code.IsAutoResendable())
                return true;

            return code switch
            {
                GimErrorCode.AckTimeout => true,
                GimErrorCode.PendingError => true,
                _ => false
            };
        }
    }
}
