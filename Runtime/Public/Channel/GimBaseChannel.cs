using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Domain.Mappers;
using Gamania.GIMChat.Internal.Domain.Models;
using Gamania.GIMChat.Internal.Domain.Message;
using Gamania.GIMChat.Internal.Domain.UseCases;
using Gamania.GIMChat.Internal.Platform.Unity;
using Logger = Gamania.GIMChat.Internal.Domain.Log.Logger;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Abstract base class for all channel types.
    /// Contains shared properties and operations (ban/mute, message retrieval, query creation).
    /// </summary>
    public abstract class GimBaseChannel
    {
        private const string TAG = "GimBaseChannel";

        /// <summary>Unique URL identifier of the channel.</summary>
        public string ChannelUrl { get; internal set; }

        /// <summary>Display name of the channel.</summary>
        public string Name { get; internal set; }

        /// <summary>Unix timestamp (milliseconds) when the channel was created.</summary>
        public long CreatedAt { get; internal set; }

        /// <summary>URL of the channel's cover image.</summary>
        public string CoverUrl { get; internal set; }

        /// <summary>Custom type for categorizing the channel.</summary>
        public string CustomType { get; internal set; }

        /// <summary>Channel type of this channel.</summary>
        protected abstract GimChannelType ChannelType { get; }

        /// <summary>Role of the current user in this channel.</summary>
        protected abstract GimRole CurrentUserRole { get; }

        /// <summary>
        /// Bans a user from this channel.
        /// </summary>
        /// <param name="userId">User ID to ban.</param>
        /// <param name="seconds">Ban duration in seconds (-1 for permanent).</param>
        /// <param name="description">Optional description.</param>
        /// <param name="handler">Completion handler with error (null on success).</param>
        public void BanUser(string userId, int seconds, string description, GimErrorHandler handler)
        {
            _ = AsyncCallbackHelper.ExecuteVoidAsync(
                () => BanUserAsync(userId, seconds, description),
                handler,
                TAG,
                "BanUser"
            );
        }

        /// <summary>
        /// Bans a user from this channel (async version).
        /// </summary>
        public async Task BanUserAsync(string userId, int seconds = -1, string description = null)
        {
            if (string.IsNullOrEmpty(userId))
                throw new GimException(GimErrorCode.InvalidParameter, "userId cannot be null or empty");

            var repository = GIMChatMain.Instance?.GetChannelRepository()
                ?? throw new GimException(GimErrorCode.ConnectionRequired, "Not connected");

            await repository.BanUserAsync(ChannelType, ChannelUrl, userId, seconds, description);
        }

        /// <summary>
        /// Unbans a user from this channel.
        /// </summary>
        public void UnbanUser(string userId, GimErrorHandler handler)
        {
            _ = AsyncCallbackHelper.ExecuteVoidAsync(
                () => UnbanUserAsync(userId),
                handler,
                TAG,
                "UnbanUser"
            );
        }

        /// <summary>
        /// Unbans a user from this channel (async version).
        /// </summary>
        public async Task UnbanUserAsync(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                throw new GimException(GimErrorCode.InvalidParameter, "userId cannot be null or empty");

            var repository = GIMChatMain.Instance?.GetChannelRepository()
                ?? throw new GimException(GimErrorCode.ConnectionRequired, "Not connected");

            await repository.UnbanUserAsync(ChannelType, ChannelUrl, userId);
        }

        /// <summary>
        /// Mutes a user in this channel.
        /// </summary>
        public void MuteUser(string userId, int seconds, string description, GimErrorHandler handler)
        {
            _ = AsyncCallbackHelper.ExecuteVoidAsync(
                () => MuteUserAsync(userId, seconds, description),
                handler,
                TAG,
                "MuteUser"
            );
        }

        /// <summary>
        /// Mutes a user in this channel (async version).
        /// </summary>
        public async Task MuteUserAsync(string userId, int seconds = -1, string description = null)
        {
            if (string.IsNullOrEmpty(userId))
                throw new GimException(GimErrorCode.InvalidParameter, "userId cannot be null or empty");

            var repository = GIMChatMain.Instance?.GetChannelRepository()
                ?? throw new GimException(GimErrorCode.ConnectionRequired, "Not connected");

            await repository.MuteUserAsync(ChannelType, ChannelUrl, userId, seconds, description);
        }

        /// <summary>
        /// Unmutes a user in this channel.
        /// </summary>
        public void UnmuteUser(string userId, GimErrorHandler handler)
        {
            _ = AsyncCallbackHelper.ExecuteVoidAsync(
                () => UnmuteUserAsync(userId),
                handler,
                TAG,
                "UnmuteUser"
            );
        }

        /// <summary>
        /// Unmutes a user in this channel (async version).
        /// </summary>
        public async Task UnmuteUserAsync(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                throw new GimException(GimErrorCode.InvalidParameter, "userId cannot be null or empty");

            var repository = GIMChatMain.Instance?.GetChannelRepository()
                ?? throw new GimException(GimErrorCode.ConnectionRequired, "Not connected");

            await repository.UnmuteUserAsync(ChannelType, ChannelUrl, userId);
        }

        /// <summary>
        /// Creates a query to fetch banned users in this channel.
        /// </summary>
        /// <param name="params">Query parameters. ChannelType and ChannelUrl will be set automatically.</param>
        /// <returns>A query for fetching banned users.</returns>
        public GimBannedUserListQuery CreateBannedUserListQuery(GimBannedUserListQueryParams @params = null)
        {
            var p = @params ?? new GimBannedUserListQueryParams();
            p.ChannelType = ChannelType;
            p.ChannelUrl = ChannelUrl;
            return GIMChat.CreateBannedUserListQuery(p);
        }

        /// <summary>
        /// Creates a query to fetch muted users in this channel.
        /// </summary>
        /// <param name="params">Query parameters. ChannelType and ChannelUrl will be set automatically.</param>
        /// <returns>A query for fetching muted users.</returns>
        public GimMutedUserListQuery CreateMutedUserListQuery(GimMutedUserListQueryParams @params = null)
        {
            var p = @params ?? new GimMutedUserListQueryParams();
            p.ChannelType = ChannelType;
            p.ChannelUrl = ChannelUrl;
            return GIMChat.CreateMutedUserListQuery(p);
        }

        protected static async Task<T> GetChannelCoreAsync<T>(
            GimChannelType gimChannelType,
            string channelUrl,
            Func<BaseChannelBO, T> mapper) where T : GimBaseChannel
        {
            if (string.IsNullOrWhiteSpace(channelUrl))
                throw new GimException(GimErrorCode.InvalidParameter, "Channel URL cannot be null or empty", "channelUrl");

            var repository = GIMChatMain.Instance?.GetChannelRepository()
                ?? throw new GimException(GimErrorCode.ConnectionRequired, "Not connected");

            try
            {
                var bo = await repository.GetChannelAsync(gimChannelType, channelUrl);
                if (bo == null)
                    throw new GimException(GimErrorCode.ErrChannelNotFound, $"Channel not found: {channelUrl}", channelUrl);

                return mapper(bo);
            }
            catch (GimException) { throw; }
            catch (Exception ex)
            {
                throw new GimException(GimErrorCode.UnknownError, "Failed to get channel", channelUrl, ex);
            }
        }

        protected static void GetChannelCore<T>(
            GimChannelType gimChannelType,
            string channelUrl,
            Func<BaseChannelBO, T> mapper,
            Action<T, GimException> callback,
            string tag,
            string operationName) where T : GimBaseChannel
        {
            if (callback == null)
            {
                Logger.Warning(tag, $"{operationName}: callback is null");
                return;
            }

            _ = AsyncCallbackHelper.ExecuteAsync(
                () => GetChannelCoreAsync(gimChannelType, channelUrl, mapper),
                (ch, err) => callback(ch, err),
                tag,
                operationName
            );
        }

        /// <summary>
        /// Sends a user message to this channel (callback version).
        /// Returns immediately with a pending message object.
        /// </summary>
        public GimUserMessage SendUserMessage(GimUserMessageCreateParams createParams, GimUserMessageHandler callback)
        {
            if (callback == null)
            {
                Logger.Warning(TAG, "SendUserMessage: callback is null");
                return null;
            }

            var pending = CreatePendingUserMessage(createParams);
            OnMessagePending(pending);

            _ = AsyncCallbackHelper.ExecuteAsync(
                () => SendUserMessageCoreAsync(createParams, pending),
                (msg, err) =>
                {
                    if (err == null) OnMessageSent(msg);
                    else OnMessageFailed(pending, err);
                    callback(msg, err);
                },
                TAG,
                "SendUserMessage"
            );
            return pending;
        }

        /// <summary>
        /// Sends a user message to this channel (async version).
        /// </summary>
        public async Task<GimUserMessage> SendUserMessageAsync(GimUserMessageCreateParams createParams)
        {
            var pending = CreatePendingUserMessage(createParams);
            OnMessagePending(pending);

            try
            {
                var sent = await SendUserMessageCoreAsync(createParams, pending);
                OnMessageSent(sent);
                return sent;
            }
            catch (GimException ex)
            {
                OnMessageFailed(pending, ex);
                throw;
            }
        }

        private async Task<GimUserMessage> SendUserMessageCoreAsync(GimUserMessageCreateParams createParams, GimUserMessage pending)
        {
            var repository = GIMChatMain.Instance?.GetMessageRepository()
                ?? throw new GimException(GimErrorCode.ConnectionRequired, "Not connected");

            var useCase = new SendMessageUseCase(repository, GetAutoResender());
            var sent = await useCase.ExecuteAsync(ChannelUrl, createParams, pending);
            return GimUserMessage.FromBase(sent);
        }

        protected virtual GimUserMessage CreatePendingUserMessage(GimUserMessageCreateParams createParams)
        {
            var currentUser = GIMChat.CurrentUser;
            return new GimUserMessage
            {
                ReqId = Guid.NewGuid().ToString("N"),
                ChannelUrl = ChannelUrl,
                Message = createParams?.Message,
                CustomType = createParams?.CustomType,
                Data = createParams?.Data,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                SendingStatus = GimSendingStatus.Pending,
                ErrorCode = null,
                Sender = currentUser == null ? null : new GimSender
                {
                    UserId = currentUser.UserId,
                    Nickname = currentUser.Nickname,
                    ProfileUrl = currentUser.ProfileUrl,
                    Role = CurrentUserRole
                }
            };
        }

        protected virtual IMessageAutoResender GetAutoResender() => null;

        protected virtual void OnMessagePending(GimUserMessage pending) { }
        protected virtual void OnMessageSent(GimUserMessage sent) { }
        protected virtual void OnMessageFailed(GimUserMessage pending, GimException error) { }

        /// <summary>
        /// Resends a failed user message (callback version).
        /// Only works for messages with resendable error codes.
        /// </summary>
        public void ResendUserMessage(GimUserMessage userMessage, GimUserMessageHandler callback)
        {
            if (callback == null)
            {
                Logger.Warning(TAG, "ResendUserMessage: callback is null");
                return;
            }

            _ = AsyncCallbackHelper.ExecuteAsync(
                () => ResendUserMessageAsync(userMessage),
                (msg, err) => callback(msg, err),
                TAG,
                "ResendUserMessage"
            );
        }

        /// <summary>
        /// Resends a failed user message (async version).
        /// Only works for messages with resendable error codes.
        /// </summary>
        public async Task<GimUserMessage> ResendUserMessageAsync(GimUserMessage userMessage)
        {
            ValidateResendable(userMessage);

            var createParams = new GimUserMessageCreateParams
            {
                Message = userMessage.Message,
                CustomType = userMessage.CustomType,
                Data = userMessage.Data
            };

            var sent = await SendUserMessageAsync(createParams);
            return GimUserMessage.FromBase(sent);
        }

        private void ValidateResendable(GimUserMessage userMessage)
        {
            if (userMessage == null)
                throw new GimException(GimErrorCode.InvalidParameter, "userMessage is null");

            if (string.IsNullOrEmpty(userMessage.ChannelUrl) || userMessage.ChannelUrl != ChannelUrl)
                throw new GimException(GimErrorCode.InvalidParameter, "message channel mismatch");

            if (userMessage.SendingStatus != GimSendingStatus.Failed)
                throw new GimException(GimErrorCode.InvalidParameter, "message is not in Failed state");

            if (!userMessage.ErrorCode.HasValue || !userMessage.ErrorCode.Value.IsResendable())
                throw new GimException(GimErrorCode.InvalidParameter, "message is not resendable");
        }

        /// <summary>
        /// Retrieves messages around a given timestamp (callback version).
        /// </summary>
        /// <param name="timestamp">Reference timestamp.</param>
        /// <param name="params">Message list parameters for pagination and filtering.</param>
        /// <param name="handler">Callback invoked with the message list or error.</param>
        public void GetMessagesByTimestamp(long timestamp, GimMessageListParams @params, GimMessageListHandler handler)
        {
            if (handler == null)
            {
                Logger.Warning(TAG, "GetMessagesByTimestamp: handler is null");
                return;
            }

            _ = AsyncCallbackHelper.ExecuteAsync(
                () => GetMessagesByTimestampAsync(timestamp, @params),
                (msgs, err) => handler(msgs, err),
                TAG,
                "GetMessagesByTimestamp"
            );
        }

        /// <summary>
        /// Retrieves messages around a given timestamp (async version).
        /// </summary>
        /// <param name="timestamp">Reference timestamp.</param>
        /// <param name="params">Message list parameters for pagination and filtering.</param>
        /// <returns>List of messages.</returns>
        public async Task<IReadOnlyList<GimBaseMessage>> GetMessagesByTimestampAsync(
            long timestamp,
            GimMessageListParams @params = null)
        {
            var repository = GIMChatMain.Instance?.GetMessageRepository()
                ?? throw new GimException(GimErrorCode.ConnectionRequired, "Not connected");

            var listParams = @params ?? new GimMessageListParams();

            var result = await repository.GetMessagesAsync(
                ChannelUrl,
                messageTs: timestamp,
                messageListParams: listParams);

            var messages = new List<GimBaseMessage>();
            foreach (var bo in result.Messages)
            {
                messages.Add(MessageBoMapper.ToPublicModel(bo));
            }
            return messages;
        }
    }
}
