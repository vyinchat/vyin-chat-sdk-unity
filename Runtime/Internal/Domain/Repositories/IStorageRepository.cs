using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Gamania.GIMChat.Internal.Domain.Repositories
{
    /// <summary>
    /// Result of single-part upload initialization.
    /// </summary>
    internal class SingleUploadInitResult
    {
        public string PresignedUrl { get; set; }
        public string ObjectId { get; set; }
        public string ContentType { get; set; }
    }

    /// <summary>
    /// Result of multipart upload initialization.
    /// </summary>
    internal class MultipartInitResult
    {
        public string ObjectId { get; set; }
        public string UploadId { get; set; }
        public List<SignedPartInfo> SignedParts { get; set; }
    }

    internal class SignedPartInfo
    {
        public string Url { get; set; }
        public int PartNo { get; set; }
    }

    /// <summary>
    /// Completed part info for multipart upload finalization.
    /// </summary>
    internal class CompletedPart
    {
        public int PartNo { get; set; }
        public string ETag { get; set; }
    }

    /// <summary>
    /// Repository interface for file storage API operations.
    /// Handles presigned URL acquisition and multipart upload lifecycle.
    /// </summary>
    internal interface IStorageRepository
    {
        /// <summary>
        /// Initiates a single-part file upload.
        /// POST /storage/file → returns presigned URL and object ID.
        /// </summary>
        Task<SingleUploadInitResult> InitSingleUploadAsync(
            string fileName,
            string fileType,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Initiates a multipart file upload.
        /// POST /storage/file/multipart/initiate → returns object ID, upload ID, and signed part URLs.
        /// </summary>
        Task<MultipartInitResult> InitMultipartUploadAsync(
            string fileName,
            int partCount,
            string fileType,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads raw bytes to a presigned URL.
        /// PUT &lt;presigned_url&gt; → returns ETag from response header.
        /// </summary>
        Task<string> UploadPartAsync(
            string signedUrl,
            byte[] data,
            string contentType,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Uploads a file to a presigned URL using streaming.
        /// PUT &lt;presigned_url&gt; → returns ETag from response header.
        /// </summary>
        Task<string> UploadPartFileAsync(
            string signedUrl,
            string filePath,
            string contentType,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Completes a multipart upload.
        /// POST /storage/file/multipart/complete → finalizes the upload.
        /// </summary>
        Task CompleteMultipartUploadAsync(
            string objectId,
            string uploadId,
            List<CompletedPart> completedParts,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Queries file processing state.
        /// GET /storage/file/{objectId}/meta → returns current status.
        /// </summary>
        Task<FileMetaResult> GetFileMetaAsync(
            string objectId,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Result of file metadata query.
    /// </summary>
    internal class FileMetaResult
    {
        public string ObjectId { get; set; }
        public string FileName { get; set; }
        public string MimeType { get; set; }
        public string Status { get; set; }
    }
}
