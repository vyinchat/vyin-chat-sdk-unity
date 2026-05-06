using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Gamania.GIMChat.Internal.Data.DTOs;
using Gamania.GIMChat.Internal.Data.Network;
using Gamania.GIMChat.Internal.Domain.Log;
using Gamania.GIMChat.Internal.Domain.Repositories;

namespace Gamania.GIMChat.Internal.Data.Repositories
{
    /// <summary>
    /// Implementation of IStorageRepository using HTTP client for file storage API calls.
    /// </summary>
    internal class StorageRepositoryImpl : IStorageRepository
    {
        private readonly IHttpClient _httpClient;
        private readonly string _baseUrl;

        public StorageRepositoryImpl(IHttpClient httpClient, string baseUrl)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _baseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
        }

        public async Task<SingleUploadInitResult> InitSingleUploadAsync(
            string fileName,
            string fileType,
            CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}/storage/file";
            var body = JsonConvert.SerializeObject(new
            {
                filename = fileName,
                file_type = fileType
            });

            Logger.Debug(LogCategory.Http, $"[Storage] InitSingleUpload: {fileName}, type={fileType}");

            var response = await _httpClient.PostAsync(url, body, cancellationToken: cancellationToken);

            if (!response.IsSuccess)
                throw GimException.FromHttpResponse(response);

            var dto = JsonConvert.DeserializeObject<SingleUploadInitResponseDTO>(response.Body);
            if (dto == null)
                throw new GimException(GimErrorCode.UnknownError, "Failed to parse single upload init response");

            var contentType = dto.fields?.GetValueOrDefault("Content-Type") ?? fileType;

            return new SingleUploadInitResult
            {
                PresignedUrl = dto.url,
                ObjectId = dto.object_id,
                ContentType = contentType
            };
        }

        public async Task<MultipartInitResult> InitMultipartUploadAsync(
            string fileName,
            int partCount,
            string fileType,
            CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}/storage/file/multipart/initiate";
            var body = JsonConvert.SerializeObject(new
            {
                filename = fileName,
                part_count = partCount,
                file_type = fileType
            });

            Logger.Debug(LogCategory.Http, $"[Storage] InitMultipartUpload: {fileName}, parts={partCount}, type={fileType}");

            var response = await _httpClient.PostAsync(url, body, cancellationToken: cancellationToken);

            if (!response.IsSuccess)
                throw GimException.FromHttpResponse(response);

            var dto = JsonConvert.DeserializeObject<MultipartInitResponseDTO>(response.Body);
            if (dto == null)
                throw new GimException(GimErrorCode.UnknownError, "Failed to parse multipart init response");

            return new MultipartInitResult
            {
                ObjectId = dto.object_id,
                UploadId = dto.upload_id,
                SignedParts = dto.sign_parts?.Select(p => new SignedPartInfo
                {
                    Url = p.url,
                    PartNo = p.part_no
                }).ToList() ?? new List<SignedPartInfo>()
            };
        }

        public async Task<string> UploadPartAsync(
            string signedUrl,
            byte[] data,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            Logger.Debug(LogCategory.Http, $"[Storage] UploadPart: {data.Length} bytes to {signedUrl.Substring(0, Math.Min(signedUrl.Length, 80))}...");

            var response = await _httpClient.PutBytesAsync(signedUrl, data, contentType, cancellationToken: cancellationToken);

            if (!response.IsSuccess)
                throw new GimException(GimErrorCode.RequestFailed, $"Upload part failed: HTTP {response.StatusCode}");

            // ETag is returned in the response header
            response.Headers.TryGetValue("ETag", out var etag);
            if (string.IsNullOrEmpty(etag))
                response.Headers.TryGetValue("etag", out etag);

            return etag ?? "";
        }

        public async Task<string> UploadPartFileAsync(
            string signedUrl,
            string filePath,
            string contentType,
            CancellationToken cancellationToken = default)
        {
            Logger.Debug(LogCategory.Http, $"[Storage] UploadPartFile: {filePath} to {signedUrl.Substring(0, Math.Min(signedUrl.Length, 80))}...");

            var response = await _httpClient.PutFileAsync(signedUrl, filePath, contentType, cancellationToken: cancellationToken);

            if (!response.IsSuccess)
                throw new GimException(GimErrorCode.RequestFailed, $"Upload part file failed: HTTP {response.StatusCode} {response.Error}");

            // ETag is returned in the response header
            response.Headers.TryGetValue("ETag", out var etag);
            if (string.IsNullOrEmpty(etag))
                response.Headers.TryGetValue("etag", out etag);

            return etag ?? "";
        }

        public async Task CompleteMultipartUploadAsync(
            string objectId,
            string uploadId,
            List<CompletedPart> completedParts,
            CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}/storage/file/multipart/complete";
            var body = JsonConvert.SerializeObject(new
            {
                object_id = objectId,
                upload_id = uploadId,
                completed_parts = completedParts.Select(p => new
                {
                    part_no = p.PartNo,
                    etag = p.ETag
                }).ToList()
            });

            Logger.Debug(LogCategory.Http, $"[Storage] CompleteMultipart: objectId={objectId}, parts={completedParts.Count}");

            var response = await _httpClient.PostAsync(url, body, cancellationToken: cancellationToken);

            if (!response.IsSuccess)
                throw GimException.FromHttpResponse(response);
        }

        public async Task<FileMetaResult> GetFileMetaAsync(
            string objectId,
            CancellationToken cancellationToken = default)
        {
            var url = $"{_baseUrl}/storage/file/{objectId}/meta";

            Logger.Debug(LogCategory.Http, $"[Storage] GetFileMeta: objectId={objectId}");

            var response = await _httpClient.GetAsync(url, cancellationToken: cancellationToken);

            if (!response.IsSuccess)
                throw GimException.FromHttpResponse(response);

            var dto = JsonConvert.DeserializeObject<FileMetaResponseDTO>(response.Body);
            if (dto == null)
                throw new GimException(GimErrorCode.UnknownError, "Failed to parse file meta response");

            return new FileMetaResult
            {
                ObjectId = dto.id,
                FileName = dto.filename,
                MimeType = dto.mime,
                Status = dto.status
            };
        }
    }
}
