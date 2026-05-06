using System;
using System.Threading;
using System.Threading.Tasks;
using Gamania.GIMChat;
using Gamania.GIMChat.Internal.Domain.Log;
using Gamania.GIMChat.Internal.Domain.Mappers;
using Gamania.GIMChat.Internal.Domain.Message;
using Gamania.GIMChat.Internal.Domain.Repositories;

namespace Gamania.GIMChat.Internal.Domain.UseCases
{
    internal class SendMessageUseCase
    {
        private readonly IMessageRepository _messageRepository;
        private readonly IMessageAutoResender _autoResender;

        public SendMessageUseCase(IMessageRepository messageRepository, IMessageAutoResender autoResender = null)
        {
            _messageRepository = messageRepository ?? throw new ArgumentNullException(nameof(messageRepository));
            _autoResender = autoResender;
        }

        public Task<GimBaseMessage> ExecuteAsync(
            string channelUrl,
            GimUserMessageCreateParams createParams,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(channelUrl, createParams, null, cancellationToken);
        }

        public async Task<GimBaseMessage> ExecuteAsync(
            string channelUrl,
            GimUserMessageCreateParams createParams,
            GimUserMessage pendingBaseMessage,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(channelUrl))
                throw new GimException(GimErrorCode.InvalidParameter, "ChannelUrl cannot be empty");
            if (createParams == null)
                throw new GimException(GimErrorCode.InvalidParameter, "createParams cannot be null");

            // Create pending message if auto-resend is enabled
            PendingMessage pendingMessage = null;
            if (_autoResender != null && _autoResender.IsEnabled)
            {
                var requestId = pendingBaseMessage?.ReqId ?? Guid.NewGuid().ToString("N");
                var baseMessage = pendingBaseMessage ?? new GimUserMessage
                {
                    ReqId = requestId,
                    ChannelUrl = channelUrl,
                    Message = createParams?.Message,
                    CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                pendingMessage = new PendingMessage(requestId, createParams, baseMessage);

                if (!_autoResender.Register(pendingMessage))
                {
                    Logger.Debug(LogCategory.Message, $"[SendMessage] Queue full or disabled: {requestId}");
                    pendingMessage = null;
                }
            }

            return await SendWithRetryHandlingAsync(pendingMessage, channelUrl, createParams, cancellationToken);
        }

        private async Task<GimBaseMessage> SendWithRetryHandlingAsync(
            PendingMessage pendingMessage,
            string channelUrl,
            GimUserMessageCreateParams createParams,
            CancellationToken cancellationToken)
        {
            pendingMessage?.MarkAsPending();

            try
            {
                var messageBO = await _messageRepository.SendMessageAsync(channelUrl, createParams, cancellationToken);
                var message = MessageBoMapper.ToPublicModel(messageBO);

                // If no pending message (auto-resend disabled), still mark as succeeded.
                if (pendingMessage == null && message != null)
                {
                    message.SendingStatus = GimSendingStatus.Succeeded;
                    message.ErrorCode = null;
                }

                if (pendingMessage != null)
                {
                    pendingMessage.MarkAsSucceeded();
                    _autoResender?.Unregister(pendingMessage.RequestId);

                    if (pendingMessage.BaseMessage != null && message != null)
                    {
                        ApplyServerFields(pendingMessage.BaseMessage, message);
                        return pendingMessage.BaseMessage;
                    }
                }
                Logger.Debug(LogCategory.Message, $"[SendMessage] Success: {pendingMessage?.RequestId ?? "no-queue"}");

                return message;
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
            // Only update ReqId if target doesn't have one (preserve original pending ReqId)
            if (string.IsNullOrEmpty(target.ReqId) && !string.IsNullOrEmpty(source.ReqId))
                target.ReqId = source.ReqId;
        }

        private GimBaseMessage HandleFailure(PendingMessage pendingMessage, GimException error)
        {
            if (pendingMessage == null)
            {
                // No auto-resend - throw immediately
                throw error;
            }

            pendingMessage.MarkAsFailed(error.ErrorCode);

            // Check if error is auto-resendable and can retry
            if (error.ErrorCode.IsAutoResendable() && pendingMessage.CanRetry())
            {
                // Keep in queue for auto-resend on reconnection
                pendingMessage.MarkAsPending();
                pendingMessage.IncrementRetry();
                Logger.Info(LogCategory.Message,
                    $"[SendMessage] Queued for resend: {pendingMessage.RequestId}, retry #{pendingMessage.RetryCount}");

                // Throw to notify caller, but message stays in queue
                throw error;
            }

            // Non-resendable error or max retries reached
            Logger.Info(LogCategory.Message,
                $"[SendMessage] Permanent failure: {pendingMessage.RequestId}, error: {error.ErrorCode}");
            throw error;
        }

        /// <summary>
        /// Resend a pending message from the queue.
        /// Called by auto-resender on reconnection.
        /// </summary>
        public async Task<GimBaseMessage> ResendAsync(PendingMessage pendingMessage, CancellationToken cancellationToken = default)
        {
            if (pendingMessage == null)
                throw new ArgumentNullException(nameof(pendingMessage));

            Logger.Info(LogCategory.Message, $"[SendMessage] Resending: {pendingMessage.RequestId}");

            var userCreateParams = pendingMessage.CreateParams as GimUserMessageCreateParams;
            if (userCreateParams == null)
                throw new GimException(GimErrorCode.InvalidParameter, "SendMessageUseCase only supports GimUserMessageCreateParams");

            return await SendWithRetryHandlingAsync(
                pendingMessage,
                pendingMessage.ChannelUrl,
                userCreateParams,
                cancellationToken);
        }
    }
}
