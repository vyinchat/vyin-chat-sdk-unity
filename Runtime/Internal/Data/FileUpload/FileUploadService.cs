using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Domain.Log;
using Gamania.GIMChat.Internal.Domain.Message;
using Gamania.GIMChat.Internal.Domain.Repositories;

namespace Gamania.GIMChat.Internal.Data.FileUpload
{
    /// <summary>
    /// Implements file upload with single-part and multipart support,
    /// progress reporting, cancellation, retry, and part cache for resume.
    /// Uses streaming (no large byte[] allocations) to align with AOS behavior.
    /// </summary>
    internal class FileUploadService : IFileUploadService
    {
        private readonly IStorageRepository _storageRepository;

        public FileUploadService(IStorageRepository storageRepository)
        {
            _storageRepository = storageRepository ?? throw new ArgumentNullException(nameof(storageRepository));
        }

        public async Task<FileUploadResult> UploadFileAsync(
            string filePath,
            string fileName,
            string mimeType,
            FileProgressHandler progressHandler,
            FilePartCache partCache,
            string requestId,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new GimException(GimErrorCode.InvalidParameter, "filePath cannot be null or empty");

            if (!File.Exists(filePath))
                throw new GimException(GimErrorCode.ErrFileNotFound, $"File not found: {filePath}");

            cancellationToken.ThrowIfCancellationRequested();

            var fileSize = new FileInfo(filePath).Length;

            Logger.Info(LogCategory.Http, $"[FileUpload] Start: {fileName}, size={fileSize}, requestId={requestId}");

            var cachedInfo = partCache?.Get(requestId);
            if (cachedInfo != null && cachedInfo.IsUploadedSuccessfully)
            {
                Logger.Info(LogCategory.Http, $"[FileUpload] Already uploaded (cached), objectId={cachedInfo.ObjectId}");
                return new FileUploadResult
                {
                    ObjectId = cachedInfo.ObjectId,
                    RequireAuth = false
                };
            }

            var progressState = new ProgressState();

            if (fileSize < FileUploadConstants.PartSize)
            {
                return await UploadSinglePartAsync(filePath, fileName, mimeType, fileSize, progressHandler, progressState, requestId, cancellationToken);
            }
            else
            {
                return await UploadMultipartAsync(filePath, fileName, mimeType, fileSize, progressHandler, progressState, partCache, requestId, cancellationToken);
            }
        }

        private async Task<FileUploadResult> UploadSinglePartAsync(
            string filePath,
            string fileName,
            string mimeType,
            long fileSize,
            FileProgressHandler progressHandler,
            ProgressState progressState,
            string requestId,
            CancellationToken cancellationToken)
        {
            Logger.Debug(LogCategory.Http, $"[FileUpload] Single-part upload (streaming): {fileName}");

            cancellationToken.ThrowIfCancellationRequested();

            var initResult = await _storageRepository.InitSingleUploadAsync(fileName, mimeType, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            ReportProgress(progressHandler, progressState, requestId, 0, 0, fileSize, filePath);

            await _storageRepository.UploadPartFileAsync(initResult.PresignedUrl, filePath, initResult.ContentType, cancellationToken);

            ReportProgress(progressHandler, progressState, requestId, fileSize, fileSize, fileSize, filePath);

            Logger.Info(LogCategory.Http, $"[FileUpload] Single-part upload complete: objectId={initResult.ObjectId}");

            return new FileUploadResult
            {
                ObjectId = initResult.ObjectId,
                RequireAuth = false
            };
        }

        private async Task<FileUploadResult> UploadMultipartAsync(
            string filePath,
            string fileName,
            string mimeType,
            long fileSize,
            FileProgressHandler progressHandler,
            ProgressState progressState,
            FilePartCache partCache,
            string requestId,
            CancellationToken cancellationToken)
        {
            var partSize = FileUploadConstants.PartSize;
            var partCount = (int)Math.Ceiling((double)fileSize / partSize);

            Logger.Debug(LogCategory.Http, $"[FileUpload] Multipart upload (streaming): {fileName}, parts={partCount}");

            var tempDir = Path.Combine(Path.GetTempPath(), "GIMChatUpload", requestId);
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

            try
            {
                var cachedInfo = partCache?.Get(requestId);
                string objectId;
                string uploadId;
                List<FilePartEntry> parts;
                var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                if (cachedInfo != null && !string.IsNullOrEmpty(cachedInfo.UploadId)
                    && !ShouldRequestNewSignUrls(cachedInfo.Parts, nowMs))
                {
                    objectId = cachedInfo.ObjectId;
                    uploadId = cachedInfo.UploadId;
                    parts = cachedInfo.Parts;
                    Logger.Info(LogCategory.Http, $"[FileUpload] Resuming multipart: objectId={objectId}, completed={parts.Count(p => p.Status == FilePartStatus.Succeeded)}/{parts.Count}");
                }
                else
                {
                    if (cachedInfo != null && !string.IsNullOrEmpty(cachedInfo.UploadId))
                    {
                        Logger.Info(LogCategory.Http, $"[FileUpload] Presigned URLs expired, re-initiating multipart for requestId={requestId}");
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    var initResult = await _storageRepository.InitMultipartUploadAsync(fileName, partCount, mimeType, cancellationToken);
                    nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    objectId = initResult.ObjectId;
                    uploadId = initResult.UploadId;
                    parts = initResult.SignedParts.Select(sp => new FilePartEntry
                    {
                        PartNo = sp.PartNo,
                        SignedUrl = sp.Url,
                        PartSize = (int)Math.Min(partSize, fileSize - (long)(sp.PartNo - 1) * partSize),
                        TimeExpired = nowMs + ParsePresignedUrlExpirySeconds(sp.Url) * 1000L,
                        Status = FilePartStatus.Scheduled
                    }).ToList();

                    if (cachedInfo?.Parts != null)
                    {
                        foreach (var cachedPart in cachedInfo.Parts)
                        {
                            if (cachedPart.Status == FilePartStatus.Succeeded)
                            {
                                var newPart = parts.FirstOrDefault(p => p.PartNo == cachedPart.PartNo);
                                if (newPart != null)
                                {
                                    newPart.Status = FilePartStatus.Succeeded;
                                    newPart.ETag = cachedPart.ETag;
                                }
                            }
                        }
                    }

                    var partInfo = new FilePartInfo
                    {
                        RequestId = requestId,
                        ObjectId = objectId,
                        UploadId = uploadId,
                        Parts = parts
                    };
                    partCache?.Put(requestId, partInfo);
                }

                await SplitFileIntoPartsAsync(filePath, tempDir, parts, cancellationToken);

                var semaphore = new SemaphoreSlim(5);
                var uploadTasks = new List<Task>();
                var lockObject = new object();

                long totalBytesSent = parts
                    .Where(p => p.Status == FilePartStatus.Succeeded)
                    .Sum(p => (long)p.PartSize);

                for (int i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    if (part.Status == FilePartStatus.Succeeded)
                        continue;

                    var partFilePath = Path.Combine(tempDir, $"part_{part.PartNo}");

                    async Task UploadPartTask(FilePartEntry p, string pPath)
                    {
                        await semaphore.WaitAsync(cancellationToken);
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            string etag = await UploadPartWithRetryAsync(p.SignedUrl, pPath, mimeType, cancellationToken);

                            lock (lockObject)
                            {
                                p.ETag = etag;
                                p.Status = FilePartStatus.Succeeded;
                                totalBytesSent += p.PartSize;

                                ReportProgress(progressHandler, progressState, requestId, p.PartSize, totalBytesSent, fileSize, filePath);
                            }
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }

                    uploadTasks.Add(UploadPartTask(part, partFilePath));
                }

                await Task.WhenAll(uploadTasks);

                cancellationToken.ThrowIfCancellationRequested();

                var completedParts = parts
                    .Where(p => p.Status == FilePartStatus.Succeeded)
                    .Select(p => new CompletedPart { PartNo = p.PartNo, ETag = p.ETag })
                    .ToList();

                await _storageRepository.CompleteMultipartUploadAsync(objectId, uploadId, completedParts, cancellationToken);

                Logger.Info(LogCategory.Http, $"[FileUpload] Multipart upload complete: objectId={objectId}");

                return new FileUploadResult
                {
                    ObjectId = objectId,
                    RequireAuth = false
                };
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch (Exception ex)
                {
                    Logger.Warning(LogCategory.Http, $"[FileUpload] Failed to cleanup temp dir: {ex.Message}");
                }
            }
        }

        private async Task SplitFileIntoPartsAsync(string sourcePath, string tempDir, List<FilePartEntry> parts, CancellationToken cancellationToken)
        {
            var bufferSize = 64 * 1024;
            var buffer = new byte[bufferSize];

            using var sourceStream = File.OpenRead(sourcePath);
            foreach (var part in parts)
            {
                if (part.Status == FilePartStatus.Succeeded) continue;

                var partFilePath = Path.Combine(tempDir, $"part_{part.PartNo}");
                if (File.Exists(partFilePath)) continue; // Already split

                cancellationToken.ThrowIfCancellationRequested();

                using var partStream = File.Create(partFilePath);
                sourceStream.Seek((long)(part.PartNo - 1) * FileUploadConstants.PartSize, SeekOrigin.Begin);

                long bytesWritten = 0;
                while (bytesWritten < part.PartSize)
                {
                    var toRead = (int)Math.Min(bufferSize, part.PartSize - bytesWritten);
                    var read = await sourceStream.ReadAsync(buffer, 0, toRead, cancellationToken);
                    if (read == 0) break;

                    await partStream.WriteAsync(buffer, 0, read, cancellationToken);
                    bytesWritten += read;
                }
            }
        }

        private async Task<string> UploadPartWithRetryAsync(
            string signedUrl,
            string filePath,
            string contentType,
            CancellationToken cancellationToken)
        {
            var maxRetry = FileUploadConstants.MaxPartRetry;

            for (int attempt = 1; attempt <= maxRetry; attempt++)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return await _storageRepository.UploadPartFileAsync(signedUrl, filePath, contentType, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    if (attempt == maxRetry)
                    {
                        Logger.Warning(LogCategory.Http, $"[FileUpload] Part upload failed after {maxRetry} attempts: {ex.Message}");
                        throw new GimException(GimErrorCode.RequestFailed, $"Part upload failed after {maxRetry} retries: {ex.Message}", ex);
                    }

                    var delayMs = (int)Math.Pow(2, attempt - 1) * 1000;
                    Logger.Debug(LogCategory.Http, $"[FileUpload] Part upload attempt {attempt} failed, retrying in {delayMs}ms");
                    await Task.Delay(delayMs, cancellationToken);
                }
            }

            throw new GimException(GimErrorCode.RequestFailed, "Part upload failed");
        }

        private async Task<byte[]> ReadFileWithProgressAsync(
            string filePath,
            long bytesToRead,
            long fileOffset,
            FileProgressHandler progressHandler,
            ProgressState progressState,
            string requestId,
            long baseTotalBytesSent,
            long totalFileSize,
            CancellationToken cancellationToken,
            bool isParallel = false)
        {
            var result = new byte[bytesToRead];
            var bufferSize = FileUploadConstants.ProgressBufferSize;
            int totalRead = 0;

            using var stream = File.OpenRead(filePath);
            stream.Seek(fileOffset, SeekOrigin.Begin);

            while (totalRead < bytesToRead)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var toRead = (int)Math.Min(bufferSize, bytesToRead - totalRead);
                var read = await stream.ReadAsync(result, totalRead, toRead, cancellationToken);
                if (read == 0) break;

                totalRead += read;

                if (!isParallel)
                {
                    var currentTotal = baseTotalBytesSent + totalRead;
                    ReportProgress(progressHandler, progressState, requestId, read, currentTotal, totalFileSize, filePath);
                }
            }

            if (totalRead < bytesToRead)
            {
                Array.Resize(ref result, totalRead);
            }

            return result;
        }

        /// <summary>
        /// Checks if presigned URLs in cached parts have expired.
        /// </summary>
        private static bool ShouldRequestNewSignUrls(List<FilePartEntry> parts, long nowMs)
        {
            if (parts == null || parts.Count == 0)
                return true;

            var firstExpiry = parts.FirstOrDefault()?.TimeExpired ?? 0;
            return firstExpiry > 0 && firstExpiry <= nowMs;
        }

        /// <summary>
        /// Parses X-Amz-Expires parameter from presigned URL.
        /// Returns the expiry duration in seconds, or 0 if not found.
        /// </summary>
        internal static long ParsePresignedUrlExpirySeconds(string url)
        {
            if (url == null) return 0;
            var match = Regex.Match(url, @"X-Amz-Expires=(\d+)");
            return match.Success && long.TryParse(match.Groups[1].Value, out var seconds) ? seconds : 0;
        }

        /// <summary>
        /// Per-upload progress throttle state. Isolated per upload to avoid cross-upload interference.
        /// </summary>
        private class ProgressState
        {
            public int LastReportedPercent;
            public readonly object Lock = new object();
        }

        private static void ReportProgress(
            FileProgressHandler handler,
            ProgressState state,
            string requestId,
            long bytesSent,
            long totalBytesSent,
            long totalBytesToSend,
            string filePath)
        {
            if (handler == null) return;

            var currentPercent = totalBytesToSend > 0
                ? (int)(totalBytesSent * 100 / totalBytesToSend)
                : 100;

            lock (state.Lock)
            {
                if (currentPercent < 100
                    && currentPercent - state.LastReportedPercent < FileUploadConstants.ProgressThrottlePercent)
                {
                    return;
                }

                state.LastReportedPercent = currentPercent;
            }

            try
            {
                handler(requestId, bytesSent, totalBytesSent, totalBytesToSend, filePath);
            }
            catch (Exception ex)
            {
                Logger.Warning(LogCategory.Http, $"[FileUpload] Progress handler error: {ex.Message}");
            }
        }
    }
}
