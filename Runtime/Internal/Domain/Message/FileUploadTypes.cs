using System.Threading;
using System.Threading.Tasks;

namespace Gamania.GIMChat.Internal.Domain.Message
{
    /// <summary>
    /// Internal progress handler delegate for file upload.
    /// </summary>
    /// <param name="requestId">Request ID of the file message.</param>
    /// <param name="bytesSent">Bytes sent in this chunk.</param>
    /// <param name="totalBytesSent">Cumulative bytes sent.</param>
    /// <param name="totalBytesToSend">Total file size.</param>
    /// <param name="filePath">Source file path.</param>
    internal delegate void FileProgressHandler(
        string requestId,
        long bytesSent,
        long totalBytesSent,
        long totalBytesToSend,
        string filePath
    );

    /// <summary>
    /// Result of a file upload operation.
    /// </summary>
    internal class FileUploadResult
    {
        /// <summary>Server-assigned object ID.</summary>
        public string ObjectId { get; set; }

        /// <summary>Whether the file URL requires auth token for access.</summary>
        public bool RequireAuth { get; set; }
    }

    /// <summary>
    /// Interface for file upload service.
    /// Handles presigned URL acquisition and file upload (single or multipart).
    /// </summary>
    internal interface IFileUploadService
    {
        /// <summary>
        /// Uploads a file to the server.
        /// Selects single-part or multipart upload based on file size vs PART_SIZE.
        /// </summary>
        /// <param name="filePath">Local file path.</param>
        /// <param name="fileName">File name for the server.</param>
        /// <param name="mimeType">MIME type of the file.</param>
        /// <param name="progressHandler">Progress callback (can be null).</param>
        /// <param name="partCache">Part cache for resume support.</param>
        /// <param name="requestId">Request ID for cache key and progress reporting.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Upload result containing objectId and requireAuth.</returns>
        Task<FileUploadResult> UploadFileAsync(
            string filePath,
            string fileName,
            string mimeType,
            FileProgressHandler progressHandler,
            FilePartCache partCache,
            string requestId,
            CancellationToken cancellationToken);
    }
}
