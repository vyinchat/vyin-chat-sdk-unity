using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Domain.Mappers;
using Gamania.GIMChat.Internal.Domain.Models;
using Gamania.GIMChat.Internal.Domain.UseCases;
using Gamania.GIMChat.Internal.Domain.Message;
using Gamania.GIMChat.Internal.Platform.Unity;
using Logger = Gamania.GIMChat.Internal.Domain.Log.Logger;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Represents a group channel for real-time messaging.
    /// </summary>
    public class GimGroupChannel : GimBaseChannel
    {
        private const string TAG = "GimGroupChannel";

        /// <inheritdoc />
        protected override GimChannelType ChannelType => GimChannelType.Group;

        /// <inheritdoc />
        protected override GimRole CurrentUserRole => MyRole;

        internal static event Action<GimGroupChannel, GimBaseMessage> InternalMessagePending;
        internal static event Action<GimGroupChannel, GimBaseMessage> InternalMessageSent;
        internal static event Action<GimGroupChannel, GimBaseMessage, GimException> InternalMessageFailed;

        #region Properties

        /// <summary>The most recent message in the channel.</summary>
        public GimBaseMessage LastMessage { get; internal set; }

        /// <summary>List of members in this channel.</summary>
        public List<GimMember> Members { get; internal set; }

        /// <summary>Total number of members in the channel.</summary>
        public int MemberCount { get; internal set; }

        /// <summary>Custom data associated with the channel (JSON string).</summary>
        public string Data { get; internal set; }

        /// <summary>Whether the channel is distinct.</summary>
        public bool IsDistinct { get; internal set; }

        /// <summary>Whether the channel is public.</summary>
        public bool IsPublic { get; internal set; }

        /// <summary>Role of current user in this channel.</summary>
        public GimRole MyRole { get; internal set; } = GimRole.None;

        /// <summary>Membership state of current user in this channel.</summary>
        public GimMemberState MyMemberState { get; internal set; } = GimMemberState.None;

        /// <summary>Muted state of current user in this channel.</summary>
        public GimMutedState MyMutedState { get; internal set; } = GimMutedState.Unmuted;

        #endregion

        #region Send Message Overrides

        protected override IMessageAutoResender GetAutoResender()
            => GIMChatMain.Instance?.GetMessageAutoResender();

        protected override void OnMessagePending(GimUserMessage pending)
            => TriggerMessagePending(this, pending);

        protected override void OnMessageSent(GimUserMessage sent)
            => TriggerMessageSent(this, sent);

        protected override void OnMessageFailed(GimUserMessage pending, GimException error)
            => TriggerMessageFailed(this, pending, error);

        #endregion

        #region Send File Message

        /// <summary>
        /// Sends a file message to this group channel (callback version, no progress).
        /// Returns immediately with a pending message object.
        /// </summary>
        public GimFileMessage SendFileMessage(GimFileMessageCreateParams createParams, GimFileMessageHandler callback)
        {
            if (callback == null)
            {
                Logger.Warning(TAG, "SendFileMessage: callback is null");
                return null;
            }

            var pending = CreatePendingFileMessage(createParams);
            TriggerMessagePending(this, pending);

            _ = AsyncCallbackHelper.ExecuteAsync(
                () => SendFileMessageCoreAsync(createParams, pending, null),
                (msg, err) =>
                {
                    if (err == null)
                        TriggerMessageSent(this, msg);
                    else
                        TriggerMessageFailed(this, pending, err);
                    callback(msg, err);
                },
                TAG,
                "SendFileMessage"
            );
            return pending;
        }

        /// <summary>
        /// Sends a file message to this group channel (callback version, with progress).
        /// Returns immediately with a pending message object.
        /// </summary>
        public GimFileMessage SendFileMessage(GimFileMessageCreateParams createParams, IGimFileMessageWithProgressHandler handler)
        {
            if (handler == null)
            {
                Logger.Warning(TAG, "SendFileMessage: handler is null");
                return null;
            }

            var pending = CreatePendingFileMessage(createParams);
            TriggerMessagePending(this, pending);

            _ = AsyncCallbackHelper.ExecuteAsync(
                () => SendFileMessageCoreAsync(createParams, pending, (reqId, bytesSent, totalBytesSent, totalBytesToSend, filePath) =>
                {
                    Internal.Platform.MainThreadDispatcher.Enqueue(() =>
                        handler.OnProgress((int)bytesSent, (int)totalBytesSent, (int)totalBytesToSend));
                }),
                (msg, err) =>
                {
                    if (err == null)
                        TriggerMessageSent(this, msg);
                    else
                        TriggerMessageFailed(this, pending, err);
                    handler.OnResult(msg, err);
                },
                TAG,
                "SendFileMessage"
            );
            return pending;
        }

        /// <summary>
        /// Sends a file message to this group channel (async version).
        /// </summary>
        public async Task<GimFileMessage> SendFileMessageAsync(GimFileMessageCreateParams createParams)
        {
            var pending = CreatePendingFileMessage(createParams);
            TriggerMessagePending(this, pending);

            try
            {
                var sent = await SendFileMessageCoreAsync(createParams, pending, null);
                TriggerMessageSent(this, sent);
                return sent;
            }
            catch (GimException ex)
            {
                TriggerMessageFailed(this, pending, ex);
                throw;
            }
        }

        private GimFileMessage CreatePendingFileMessage(GimFileMessageCreateParams createParams)
        {
            return new GimFileMessage
            {
                ReqId = Guid.NewGuid().ToString("N"),
                ChannelUrl = ChannelUrl,
                Name = createParams?.FileName ?? "",
                MimeType = createParams?.MimeType ?? "",
                Size = createParams?.FileSize ?? 0,
                CustomType = createParams?.CustomType,
                Data = createParams?.Data,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                SendingStatus = GimSendingStatus.Pending,
                ErrorCode = null,
                Sender = BuildPendingSender(MyRole),
                FileMessageCreateParams = createParams
            };
        }

        private static GimSender BuildPendingSender(GimRole role)
        {
            var currentUser = GIMChat.CurrentUser;
            if (currentUser == null)
            {
                return null;
            }

            return new GimSender
            {
                UserId = currentUser.UserId,
                Nickname = currentUser.Nickname,
                ProfileUrl = currentUser.ProfileUrl,
                Role = role
            };
        }

        private async Task<GimFileMessage> SendFileMessageCoreAsync(
            GimFileMessageCreateParams createParams,
            GimFileMessage pending,
            Internal.Domain.Message.FileProgressHandler progressHandler)
        {
            var repository = GIMChatMain.Instance?.GetMessageRepository()
                ?? throw new GimException(GimErrorCode.ConnectionRequired, "Not connected");
            var autoResender = GIMChatMain.Instance?.GetMessageAutoResender();
            var uploadService = GIMChatMain.Instance?.GetFileUploadService();
            var partCache = GIMChatMain.Instance?.GetFilePartCache();

            var storageRepository = GIMChatMain.Instance?.GetStorageRepository();

            var useCase = new SendFileMessageUseCase(repository, uploadService, autoResender, partCache, storageRepository);
            var sent = await useCase.ExecuteAsync(ChannelUrl, createParams, pending, progressHandler);
            return GimFileMessage.FromBase(sent);
        }

        #endregion

        #region Resend File Message

        /// <summary>
        /// Resends a failed file message (callback version).
        /// </summary>
        public void ResendFileMessage(GimFileMessage fileMessage, GimFileMessageHandler callback)
        {
            if (callback == null)
            {
                Logger.Warning(TAG, "ResendFileMessage: callback is null");
                return;
            }

            _ = AsyncCallbackHelper.ExecuteAsync(
                () => ResendFileMessageAsync(fileMessage),
                (msg, err) => callback(msg, err),
                TAG,
                "ResendFileMessage"
            );
        }

        /// <summary>
        /// Resends a failed file message (callback version, with progress).
        /// </summary>
        public void ResendFileMessage(GimFileMessage fileMessage, IGimFileMessageWithProgressHandler handler)
        {
            if (handler == null)
            {
                Logger.Warning(TAG, "ResendFileMessage: handler is null");
                return;
            }

            _ = AsyncCallbackHelper.ExecuteAsync(
                () => ResendFileMessageCoreAsync(fileMessage, (reqId, bytesSent, totalBytesSent, totalBytesToSend, filePath) =>
                {
                    Internal.Platform.MainThreadDispatcher.Enqueue(() =>
                        handler.OnProgress((int)bytesSent, (int)totalBytesSent, (int)totalBytesToSend));
                }),
                (msg, err) => handler.OnResult(msg, err),
                TAG,
                "ResendFileMessage"
            );
        }

        /// <summary>
        /// Resends a failed file message (async version).
        /// </summary>
        public async Task<GimFileMessage> ResendFileMessageAsync(GimFileMessage fileMessage)
        {
            return await ResendFileMessageCoreAsync(fileMessage, null);
        }

        private async Task<GimFileMessage> ResendFileMessageCoreAsync(
            GimFileMessage fileMessage,
            Internal.Domain.Message.FileProgressHandler progressHandler)
        {
            ValidateFileResendable(fileMessage);

            var createParams = fileMessage.FileMessageCreateParams ?? new GimFileMessageCreateParams
            {
                FilePath = null,
                FileUrl = fileMessage.PlainUrl,
                FileName = fileMessage.Name,
                MimeType = fileMessage.MimeType,
                FileSize = fileMessage.Size,
                CustomType = fileMessage.CustomType,
                Data = fileMessage.Data
            };

            var repository = GIMChatMain.Instance?.GetMessageRepository()
                ?? throw new GimException(GimErrorCode.ConnectionRequired, "Not connected");
            var autoResender = GIMChatMain.Instance?.GetMessageAutoResender();
            var uploadService = GIMChatMain.Instance?.GetFileUploadService();
            var partCache = GIMChatMain.Instance?.GetFilePartCache();

            var useCase = new SendFileMessageUseCase(repository, uploadService, autoResender, partCache);
            var sent = await useCase.ExecuteAsync(ChannelUrl, createParams, fileMessage, progressHandler);
            return GimFileMessage.FromBase(sent);
        }

        private void ValidateFileResendable(GimBaseMessage message)
        {
            if (message == null)
                throw new GimException(GimErrorCode.InvalidParameter, "message is null");

            if (string.IsNullOrEmpty(message.ChannelUrl) || message.ChannelUrl != ChannelUrl)
                throw new GimException(GimErrorCode.InvalidParameter, "message channel mismatch");

            if (message.SendingStatus != GimSendingStatus.Failed &&
                message.SendingStatus != GimSendingStatus.Canceled)
                throw new GimException(GimErrorCode.InvalidParameter, "message is not in Failed or Canceled state");

            if (message.SendingStatus == GimSendingStatus.Failed &&
                message.ErrorCode.HasValue && !message.ErrorCode.Value.IsResendable())
                throw new GimException(GimErrorCode.InvalidParameter, "message is not resendable");
        }

        #endregion

        #region Channel Event Handlers

        private static readonly ConcurrentDictionary<string, GimGroupChannelHandler> _handlers = new();

        /// <summary>
        /// Adds a group channel handler to receive message events.
        /// </summary>
        /// <param name="handlerId">Unique identifier for this handler.</param>
        /// <param name="handler">Handler containing callback functions.</param>
        public static void AddGroupChannelHandler(string handlerId, GimGroupChannelHandler handler)
        {
            if (!_handlers.TryAdd(handlerId, handler))
            {
                Logger.Warning(TAG, $"Handler already exists: {handlerId}");
                return;
            }

            Logger.Debug(TAG, $"Added group channel handler: {handlerId}");
        }

        /// <summary>
        /// Returns the handler registered with the given id, or null if not found.
        /// </summary>
        public static GimGroupChannelHandler GetGroupChannelHandler(string handlerId)
        {
            return _handlers.TryGetValue(handlerId, out var handler) ? handler : null;
        }

        /// <summary>
        /// Removes a group channel handler.
        /// </summary>
        /// <param name="handlerId">Unique identifier of the handler to remove.</param>
        public static void RemoveGroupChannelHandler(string handlerId)
        {
            if (_handlers.TryRemove(handlerId, out _))
            {
                Logger.Debug(TAG, $"Removed group channel handler: {handlerId}");
            }
            else
            {
                Logger.Warning(TAG, $"Handler not found: {handlerId}");
            }
        }

        /// <summary>
        /// Removes all registered group channel handlers.
        /// </summary>
        public static void RemoveAllGroupChannelHandlers()
        {
            var count = _handlers.Count;
            _handlers.Clear();
            Logger.Debug(TAG, $"Removed all group channel handlers (count: {count})");
        }

        internal static void TriggerMessageReceived(GimGroupChannel channel, GimBaseMessage message)
        {
            foreach (var handler in _handlers.Values)
            {
                try
                {
                    handler.OnMessageReceived?.Invoke(channel, message);
                }
                catch (Exception e)
                {
                    Logger.Error(TAG, "Error in OnMessageReceived handler", e);
                }
            }
        }

        internal static void TriggerMessageUpdated(GimGroupChannel channel, GimBaseMessage message)
        {
            foreach (var handler in _handlers.Values)
            {
                try
                {
                    handler.OnMessageUpdated?.Invoke(channel, message);
                }
                catch (Exception e)
                {
                    Logger.Error(TAG, "Error in OnMessageUpdated handler", e);
                }
            }
        }

        internal static void TriggerChannelChanged(GimGroupChannel channel)
        {
            foreach (var handler in _handlers.Values)
            {
                try
                {
                    handler.OnChannelChanged?.Invoke(channel);
                }
                catch (Exception e)
                {
                    Logger.Error(TAG, "Error in OnChannelChanged handler", e);
                }
            }
        }

        internal static void TriggerChannelDeleted(string channelUrl)
        {
            foreach (var handler in _handlers.Values)
            {
                try
                {
                    handler.OnChannelDeleted?.Invoke(channelUrl);
                }
                catch (Exception e)
                {
                    Logger.Error(TAG, "Error in OnChannelDeleted handler", e);
                }
            }
        }

        internal static void TriggerMessagePending(GimGroupChannel channel, GimBaseMessage message)
        {
            try
            {
                InternalMessagePending?.Invoke(channel, message);
            }
            catch (Exception e)
            {
                Logger.Error(TAG, "Error in InternalMessagePending event", e);
            }
        }

        internal static void TriggerMessageSent(GimGroupChannel channel, GimBaseMessage message)
        {
            try
            {
                InternalMessageSent?.Invoke(channel, message);
            }
            catch (Exception e)
            {
                Logger.Error(TAG, "Error in InternalMessageSent event", e);
            }
        }

        internal static void TriggerMessageFailed(GimGroupChannel channel, GimBaseMessage message, GimException error)
        {
            try
            {
                InternalMessageFailed?.Invoke(channel, message, error);
            }
            catch (Exception e)
            {
                Logger.Error(TAG, "Error in InternalMessageFailed event", e);
            }
        }

        #endregion

        #region Static Channel Operations

        public static Task<GimGroupChannel> GetChannelAsync(string channelUrl)
            => GetChannelCoreAsync(GimChannelType.Group, channelUrl,
                bo => GroupChannelBoMapper.ToPublicModel((GroupChannelBO)bo));

        public static void GetChannel(string channelUrl, GimGroupChannelCallbackHandler callback)
            => GetChannelCore(GimChannelType.Group, channelUrl,
                bo => GroupChannelBoMapper.ToPublicModel((GroupChannelBO)bo),
                (ch, err) => callback?.Invoke(ch, err),
                TAG, "GetChannel");

        public static async Task<GimGroupChannel> CreateChannelAsync(GimGroupChannelCreateParams createParams)
        {
            var repository = GIMChatMain.Instance.GetChannelRepository();
            var useCase = new CreateChannelUseCase(repository);
            return await useCase.ExecuteAsync(createParams);
        }

        public static void CreateChannel(GimGroupChannelCreateParams createParams, GimGroupChannelCallbackHandler callback)
        {
            if (callback == null)
            {
                Logger.Warning(TAG, "CreateChannel: callback is null");
                return;
            }

            _ = AsyncCallbackHelper.ExecuteAsync(
                () => CreateChannelAsync(createParams),
                (ch, err) => callback(ch, err),
                TAG,
                "CreateChannel"
            );
        }

        [Obsolete("Use CreateChannelAsync or CreateChannel instead")]
        public static void CreateGroupChannel(GimGroupChannelCreateParams channelCreateParams, Action<string, string> callback)
        {
            if (callback == null)
            {
                Logger.Warning(TAG, "CreateGroupChannel: callback is null");
                return;
            }

            if (channelCreateParams == null)
            {
                callback.Invoke(null, "channelCreateParams is null");
                return;
            }

            GimGroupChannelCallbackHandler handler = (channel, error) =>
            {
                if (error != null) { callback.Invoke(null, error.Message); return; }
                string result = $"{{\"channelUrl\":\"{channel?.ChannelUrl}\",\"name\":\"{channel?.Name}\"}}";
                callback.Invoke(result, null);
            };

            CreateChannel(channelCreateParams, handler);
        }

        [Obsolete("Use GetChannelAsync instead")]
        public static Task<GimGroupChannel> GetGroupChannelAsync(string channelUrl)
            => GetChannelAsync(channelUrl);

        [Obsolete("Use GetChannel instead")]
        public static void GetGroupChannel(string channelUrl, GimGroupChannelCallbackHandler callback)
            => GetChannel(channelUrl, callback);

        [Obsolete("Use CreateChannelAsync instead")]
        public static Task<GimGroupChannel> CreateGroupChannelAsync(GimGroupChannelCreateParams createParams)
            => CreateChannelAsync(createParams);

        [Obsolete("Use CreateChannel instead")]
        public static void CreateGroupChannel(GimGroupChannelCreateParams createParams, GimGroupChannelCallbackHandler callback)
            => CreateChannel(createParams, callback);

        #endregion
    }
}
