namespace Gamania.GIMChat.Internal.Domain.Message
{
    /// <summary>
    /// Constants for file upload.
    /// </summary>
    internal static class FileUploadConstants
    {
        /// <summary>
        /// Part size threshold and chunk size for multipart upload (5MB).
        /// Files smaller than this use single-part upload.
        /// </summary>
        public const int PartSize = 5 * 1024 * 1024;

        /// <summary>
        /// Buffer size for streaming file reads during upload (1KB).
        /// Progress callback fires after each buffer read.
        /// </summary>
        public const int ProgressBufferSize = 1024;

        /// <summary>
        /// Minimum percentage change between progress callback invocations.
        /// Prevents excessive UI updates during upload.
        /// </summary>
        public const int ProgressThrottlePercent = 3;

        /// <summary>
        /// Maximum retry attempts per upload part.
        /// </summary>
        public const int MaxPartRetry = 3;

        /// <summary>
        /// Default upload size limit in bytes (0 = no limit).
        /// Server may override via AppInfo in the future.
        /// </summary>
        public const long DefaultUploadSizeLimit = 0;
    }
}
