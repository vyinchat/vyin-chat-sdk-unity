using System.Collections.Generic;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Parameters for creating a file message.
    /// </summary>
    public class GimFileMessageCreateParams : GimBaseMessageCreateParams
    {
        /// <summary>
        /// Local file path to upload. Mutually exclusive with FileUrl.
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Existing remote file URL. Mutually exclusive with FilePath.
        /// When set, the SDK skips upload and sends the FILE command directly.
        /// </summary>
        public string FileUrl { get; set; }

        /// <summary>
        /// Override file name. Auto-detected from FilePath if null.
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// Override MIME type. Auto-detected from file extension if null.
        /// </summary>
        public string MimeType { get; set; }

        /// <summary>
        /// Override file size in bytes. Auto-detected from file if null.
        /// </summary>
        public long? FileSize { get; set; }

        /// <summary>
        /// Requested thumbnail dimensions. Server generates actual thumbnails.
        /// </summary>
        public List<GimThumbnailSize> ThumbnailSizes { get; set; }
    }
}
