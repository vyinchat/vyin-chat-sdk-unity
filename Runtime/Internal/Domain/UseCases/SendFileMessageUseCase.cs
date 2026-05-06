using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Domain.Log;
using Gamania.GIMChat.Internal.Domain.Mappers;
using Gamania.GIMChat.Internal.Domain.Message;
using Gamania.GIMChat.Internal.Domain.Repositories;

namespace Gamania.GIMChat.Internal.Domain.UseCases
{
    /// <summary>
    /// Orchestrates file message sending: validate → upload → FILE command → ACK.
    /// Integrates with auto-resend queue and part cache for resume.
    /// </summary>
    internal class SendFileMessageUseCase
    {
        private readonly IMessageRepository _messageRepository;
        private readonly IFileUploadService _uploadService;
        private readonly IStorageRepository _storageRepository;
        private readonly IMessageAutoResender _autoResender;
        private readonly FilePartCache _partCache;
        private readonly long _uploadSizeLimit;
        private static readonly TimeSpan FileReadyTimeout = TimeSpan.FromSeconds(60);

        public SendFileMessageUseCase(
            IMessageRepository messageRepository,
            IFileUploadService uploadService,
            IMessageAutoResender autoResender,
            FilePartCache partCache,
            IStorageRepository storageRepository = null,
            long uploadSizeLimit = 0)
        {
            _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
            _uploadService = uploadService;
            _storageRepository = storageRepository;
            _autoResender = autoResender;
            _partCache = partCache ?? new FilePartCache();
            _uploadSizeLimit = uploadSizeLimit > 0 ? uploadSizeLimit : long.MaxValue;
        }

        /// <summary>
        /// Executes file message send flow.
        /// </summary>
        public async Task<GimBaseMessage> ExecuteAsync(
            string channelUrl,
            GimFileMessageCreateParams createParams,
            GimFileMessage pendingBaseMessage,
            FileProgressHandler progressHandler,
            CancellationToken cancellationToken = default)
        {
            ValidateParams(channelUrl, createParams);

            // Auto-detect file metadata
            AutoDetectFileMetadata(createParams);

            // Sync auto-detected fields back to the pending message
            // (it was created before auto-detect ran)
            if (pendingBaseMessage != null)
            {
                if (string.IsNullOrEmpty(pendingBaseMessage.Name))
                    pendingBaseMessage.Name = createParams.FileName ?? "";
                if (string.IsNullOrEmpty(pendingBaseMessage.MimeType))
                    pendingBaseMessage.MimeType = createParams.MimeType ?? "";
                if (pendingBaseMessage.Size == 0 && createParams.FileSize.HasValue)
                    pendingBaseMessage.Size = createParams.FileSize.Value;
            }

            // Register with auto-resend queue
            PendingMessage pendingMessage = null;
            if (_autoResender != null && _autoResender.IsEnabled)
            {
                var requestId = pendingBaseMessage?.ReqId ?? Guid.NewGuid().ToString("N");
                var baseMessage = pendingBaseMessage ?? CreatePendingFileMessage(requestId, channelUrl, createParams);

                pendingMessage = new PendingMessage(requestId, createParams, baseMessage);

                if (!_autoResender.Register(pendingMessage))
                {
                    Logger.Debug(LogCategory.Message, $"[SendFileMessage] Queue full or disabled: {requestId}");
                    pendingMessage = null;
                }
            }

            return await SendWithRetryHandlingAsync(
                pendingMessage, channelUrl, createParams, progressHandler, cancellationToken);
        }

        /// <summary>
        /// Resend a pending file message from the queue.
        /// Called by auto-resender on reconnection.
        /// </summary>
        public async Task<GimBaseMessage> ResendAsync(
            PendingMessage pendingMessage,
            CancellationToken cancellationToken = default)
        {
            if (pendingMessage == null)
                throw new ArgumentNullException(nameof(pendingMessage));

            var fileCreateParams = pendingMessage.CreateParams as GimFileMessageCreateParams;
            if (fileCreateParams == null)
                throw new GimException(GimErrorCode.InvalidParameter, "SendFileMessageUseCase only supports GimFileMessageCreateParams");

            Logger.Info(LogCategory.Message, $"[SendFileMessage] Resending: {pendingMessage.RequestId}");

            return await SendWithRetryHandlingAsync(
                pendingMessage,
                pendingMessage.ChannelUrl,
                fileCreateParams,
                null, // No progress handler on auto-resend
                cancellationToken);
        }

        private async Task<GimBaseMessage> SendWithRetryHandlingAsync(
            PendingMessage pendingMessage,
            string channelUrl,
            GimFileMessageCreateParams createParams,
            FileProgressHandler progressHandler,
            CancellationToken cancellationToken)
        {
            pendingMessage?.MarkAsPending();

            try
            {
                string objectId = null;
                string fileUrl = createParams.FileUrl;

                // Upload phase (only if FilePath is provided)
                if (!string.IsNullOrEmpty(createParams.FilePath))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var requestId = pendingMessage?.RequestId ?? Guid.NewGuid().ToString("N");
                    var uploadResult = await _uploadService.UploadFileAsync(
                        createParams.FilePath,
                        createParams.FileName,
                        createParams.MimeType,
                        progressHandler,
                        _partCache,
                        requestId,
                        cancellationToken);

                    objectId = uploadResult.ObjectId;

                    // Poll until server finishes processing.
                    // Must complete BEFORE sending FILE command so the ACK returns full fields.
                    if (!string.IsNullOrEmpty(objectId) && _storageRepository != null)
                    {
                        await PollFileReadyAsync(objectId, createParams.FileSize ?? 0, cancellationToken);
                    }
                }

                // FILE command phase
                cancellationToken.ThrowIfCancellationRequested();

                var messageBO = await _messageRepository.SendFileMessageAsync(
                    channelUrl,
                    objectId,
                    fileUrl,
                    createParams.FileName,
                    createParams.MimeType,
                    createParams.FileSize ?? 0,
                    createParams,
                    cancellationToken);

                var message = MessageBoMapper.ToPublicModel(messageBO);

                if (pendingMessage == null && message != null)
                {
                    message.SendingStatus = GimSendingStatus.Succeeded;
                    message.ErrorCode = null;
                }

                if (pendingMessage != null)
                {
                    pendingMessage.MarkAsSucceeded();
                    _autoResender?.Unregister(pendingMessage.RequestId);
                    _partCache.Remove(pendingMessage.RequestId);

                    if (pendingMessage.BaseMessage != null && message != null)
                    {
                        ApplyServerFields(pendingMessage.BaseMessage, message);

                        // Apply file-specific fields from server response
                        if (pendingMessage.BaseMessage is GimFileMessage pendingFile)
                        {
                            pendingFile.FileMessageCreateParams = createParams;
                            if (!string.IsNullOrEmpty(objectId))
                            {
                                pendingFile.ObjectId = objectId;
                            }

                            if (message is GimFileMessage serverFile)
                            {
                                pendingFile.PlainUrl = serverFile.PlainUrl;
                                pendingFile.RequireAuth = serverFile.RequireAuth;
                                pendingFile.Thumbnails = serverFile.Thumbnails;
                                pendingFile.ObjectStatus = serverFile.ObjectStatus;
                                if (!string.IsNullOrEmpty(serverFile.ObjectId))
                                    pendingFile.ObjectId = serverFile.ObjectId;
                            }
                        }

                        return pendingMessage.BaseMessage;
                    }
                }

                Logger.Debug(LogCategory.Message, $"[SendFileMessage] Success: {pendingMessage?.RequestId ?? "no-queue"}");
                return message;
            }
            catch (OperationCanceledException)
            {
                HandleCancellation(pendingMessage);
                throw new GimException(GimErrorCode.FileUploadCanceled, "File upload was canceled");
            }
            catch (GimException vcEx)
            {
                return HandleFailure(pendingMessage, vcEx);
            }
            catch (Exception ex)
            {
                var fallback = new GimException(GimErrorCode.UnknownError, $"Unexpected error: {ex.Message}", ex);
                return HandleFailure(pendingMessage, fallback);
            }
        }

        private void HandleCancellation(PendingMessage pendingMessage)
        {
            if (pendingMessage != null)
            {
                pendingMessage.MarkAsCanceled();
                _autoResender?.Unregister(pendingMessage.RequestId);
            }
        }

        private GimBaseMessage HandleFailure(PendingMessage pendingMessage, GimException error)
        {
            if (pendingMessage == null)
                throw error;

            pendingMessage.MarkAsFailed(error.ErrorCode);

            if (error.ErrorCode.IsAutoResendable() && pendingMessage.CanRetry())
            {
                pendingMessage.MarkAsPending();
                pendingMessage.IncrementRetry();
                Logger.Info(LogCategory.Message,
                    $"[SendFileMessage] Queued for resend: {pendingMessage.RequestId}, retry #{pendingMessage.RetryCount}");
                throw error;
            }

            Logger.Info(LogCategory.Message,
                $"[SendFileMessage] Permanent failure: {pendingMessage.RequestId}, error: {error.ErrorCode}");
            throw error;
        }

        private static void ApplyServerFields(GimBaseMessage target, GimBaseMessage source)
        {
            target.MessageId = source.MessageId;
            target.Message = source.Message;
            target.ChannelUrl = source.ChannelUrl;
            target.CreatedAt = source.CreatedAt;
            target.Done = source.Done;
            target.CustomType = source.CustomType;
            target.Data = source.Data;
            target.Sender = source.Sender;
            if (string.IsNullOrEmpty(target.ReqId) && !string.IsNullOrEmpty(source.ReqId))
                target.ReqId = source.ReqId;
        }

        private void ValidateParams(string channelUrl, GimFileMessageCreateParams createParams)
        {
            if (string.IsNullOrEmpty(channelUrl))
                throw new GimException(GimErrorCode.InvalidParameter, "ChannelUrl cannot be empty");
            if (createParams == null)
                throw new GimException(GimErrorCode.InvalidParameter, "createParams cannot be null");
            if (string.IsNullOrEmpty(createParams.FilePath) && string.IsNullOrEmpty(createParams.FileUrl))
                throw new GimException(GimErrorCode.InvalidParameter, "Either FilePath or FileUrl must be provided");
            if (!string.IsNullOrEmpty(createParams.FilePath) && !string.IsNullOrEmpty(createParams.FileUrl))
                throw new GimException(GimErrorCode.InvalidParameter, "FilePath and FileUrl are mutually exclusive");

            // Validate file exists and size
            if (!string.IsNullOrEmpty(createParams.FilePath))
            {
                if (!File.Exists(createParams.FilePath))
                    throw new GimException(GimErrorCode.ErrFileNotFound, $"File not found: {createParams.FilePath}");

                var fileSize = new FileInfo(createParams.FilePath).Length;
                if (fileSize > _uploadSizeLimit)
                    throw new GimException(GimErrorCode.FileSizeLimitExceeded,
                        $"File size {fileSize} exceeds upload limit {_uploadSizeLimit}");
            }
        }

        private static void AutoDetectFileMetadata(GimFileMessageCreateParams createParams)
        {
            if (string.IsNullOrEmpty(createParams.FilePath))
                return;

            if (string.IsNullOrEmpty(createParams.FileName))
                createParams.FileName = Path.GetFileName(createParams.FilePath);

            if (string.IsNullOrEmpty(createParams.MimeType))
                createParams.MimeType = DetectMimeType(createParams.FileName);

            if (!createParams.FileSize.HasValue)
            {
                if (File.Exists(createParams.FilePath))
                {
                    createParams.FileSize = new FileInfo(createParams.FilePath).Length;
                }
            }
        }

        private static string DetectMimeType(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "application/octet-stream";

            var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
            return ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                ".svg" => "image/svg+xml",
                ".mp4" => "video/mp4",
                ".mov" => "video/quicktime",
                ".avi" => "video/x-msvideo",
                ".webm" => "video/webm",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".ogg" => "audio/ogg",
                ".pdf" => "application/pdf",
                ".zip" => "application/zip",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".txt" => "text/plain",
                ".html" or ".htm" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                _ => "application/octet-stream"
            };
        }

        /// <summary>
        /// Polls GET /storage/file/{objectId}/meta until status becomes "ready" or "error".
        /// Must complete BEFORE sending FILE command so the ACK returns full fields.
        /// </summary>
        private async Task PollFileReadyAsync(
            string objectId,
            long fileSize,
            CancellationToken cancellationToken)
        {
            // Polling interval based on file size
            int intervalMs;
            if (fileSize < 5 * 1024 * 1024) // < 5MB
                intervalMs = 1000;
            else if (fileSize < 50L * 1024 * 1024) // < 50MB
                intervalMs = 3000;
            else
                intervalMs = 5000;

            var deadline = DateTime.UtcNow.Add(FileReadyTimeout);

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await Task.Delay(intervalMs, cancellationToken);

                try
                {
                    var meta = await _storageRepository.GetFileMetaAsync(objectId, cancellationToken);
                    var status = meta?.Status ?? "";

                    Logger.Debug(LogCategory.Message,
                        $"[SendFileMessage] Poll objectId={objectId}, status={status}");

                    if (status == "ready")
                        return;

                    if (status == "error")
                        throw new GimException(GimErrorCode.ErrFileIsNotReady,
                            $"Server reported file processing error for objectId={objectId}");
                }
                catch (GimException) { throw; }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Logger.Warning(LogCategory.Message,
                        $"[SendFileMessage] Poll error objectId={objectId}: {ex.Message}");
                }
            }

            throw new GimException(GimErrorCode.ErrFileIsNotReady,
                $"File processing timeout: status did not become 'ready' within {FileReadyTimeout.TotalSeconds}s");
        }

        private static GimFileMessage CreatePendingFileMessage(string requestId, string channelUrl, GimFileMessageCreateParams createParams)
        {
            return new GimFileMessage
            {
                ReqId = requestId,
                ChannelUrl = channelUrl,
                Name = createParams.FileName ?? "",
                MimeType = createParams.MimeType ?? "",
                Size = createParams.FileSize ?? 0,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                SendingStatus = GimSendingStatus.Pending,
                FileMessageCreateParams = createParams
            };
        }
    }
}
