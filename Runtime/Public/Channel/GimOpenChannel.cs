using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Domain.Log;
using Gamania.GIMChat.Internal.Domain.Mappers;
using Gamania.GIMChat.Internal.Domain.Message;
using Gamania.GIMChat.Internal.Domain.Models;
using Gamania.GIMChat.Internal.Domain.UseCases;
using Gamania.GIMChat.Internal.Platform.Unity;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Represents an open channel for large-scale public messaging.
    /// Unlike group channels, users must explicitly enter/exit open channels.
    /// Messages are only received when the channel is entered.
    /// </summary>
    public class GimOpenChannel : GimBaseChannel
    {
        private const string TAG = "GimOpenChannel";

        /// <inheritdoc />
        protected override GimChannelType ChannelType => GimChannelType.Open;

        /// <inheritdoc />
        protected override GimRole CurrentUserRole
        {
            get
            {
                var currentUser = GIMChat.CurrentUser;
                return currentUser != null && IsOperator(currentUser.UserId)
                    ? GimRole.Operator
                    : GimRole.None;
            }
        }

        #region Properties

        public string Data { get; internal set; }
        public bool IsFrozen { get; internal set; }
        public int ParticipantCount { get; internal set; }
        public IReadOnlyList<GimUser> Operators { get; internal set; } = new List<GimUser>();

        #endregion

        #region Instance Methods

        public bool IsEntered => IsEnteredChannel(ChannelUrl);

        public bool IsOperator(string userId)
        {
            if (string.IsNullOrEmpty(userId) || Operators == null) return false;
            foreach (var op in Operators)
            {
                if (op?.UserId == userId) return true;
            }
            return false;
        }

        /// <summary>
        /// Enters this open channel to start receiving messages.
        /// </summary>
        public void Enter(GimErrorHandler handler)
        {
            _ = AsyncCallbackHelper.ExecuteVoidAsync(
                () => EnterAsync(),
                handler,
                TAG,
                "Enter"
            );
        }

        /// <summary>
        /// Enters this open channel to start receiving messages (async version).
        /// </summary>
        public async Task EnterAsync()
        {
            var repository = GIMChatMain.Instance?.GetChannelRepository()
                ?? throw new GimException(GimErrorCode.ConnectionRequired, "Not connected");

            var participantCount = await repository.EnterChannelAsync(ChannelUrl);
            ParticipantCount = participantCount;
            AddEnteredChannel(this);
        }

        /// <summary>
        /// Exits this open channel to stop receiving messages.
        /// </summary>
        public void Exit(GimErrorHandler handler)
        {
            _ = AsyncCallbackHelper.ExecuteVoidAsync(
                () => ExitAsync(),
                handler,
                TAG,
                "Exit"
            );
        }

        /// <summary>
        /// Exits this open channel to stop receiving messages (async version).
        /// </summary>
        public async Task ExitAsync()
        {
            var repository = GIMChatMain.Instance?.GetChannelRepository()
                ?? throw new GimException(GimErrorCode.ConnectionRequired, "Not connected");

            var participantCount = await repository.ExitChannelAsync(ChannelUrl);
            ParticipantCount = participantCount;
            RemoveEnteredChannel(ChannelUrl);
        }

        /// <summary>
        /// Refreshes this channel's data from the server.
        /// </summary>
        public void Refresh(Action<GimOpenChannel, GimException> handler)
        {
            _ = AsyncCallbackHelper.ExecuteAsync(
                () => RefreshAsync(),
                (ch, err) => handler?.Invoke(ch, err),
                TAG,
                "Refresh"
            );
        }

        /// <summary>
        /// Refreshes this channel's data from the server (async version).
        /// </summary>
        public async Task<GimOpenChannel> RefreshAsync()
        {
            var repository = GIMChatMain.Instance?.GetChannelRepository()
                ?? throw new GimException(GimErrorCode.ConnectionRequired, "Not connected");

            var bo = await repository.GetChannelAsync(GimChannelType.Open, ChannelUrl) as OpenChannelBO;
            if (bo != null)
                ApplyChannelData(OpenGroupChannelBoMapper.ToPublicModel(bo));
            return this;
        }

        private void ApplyChannelData(GimOpenChannel source)
        {
            Name = source.Name;
            CoverUrl = source.CoverUrl;
            Data = source.Data;
            CustomType = source.CustomType;
            IsFrozen = source.IsFrozen;
            ParticipantCount = source.ParticipantCount;
            Operators = source.Operators;
            if (IsEntered) AddEnteredChannel(this);
        }

        /// <summary>
        /// Updates this channel's properties.
        /// </summary>
        public void UpdateChannel(GimOpenChannelUpdateParams @params, Action<GimOpenChannel, GimException> handler)
        {
            _ = AsyncCallbackHelper.ExecuteAsync(
                () => UpdateChannelAsync(@params),
                (ch, err) => handler?.Invoke(ch, err),
                TAG,
                "UpdateChannel"
            );
        }

        /// <summary>
        /// Updates this channel's properties (async version).
        /// </summary>
        public async Task<GimOpenChannel> UpdateChannelAsync(GimOpenChannelUpdateParams @params)
        {
            var repository = GIMChatMain.Instance?.GetChannelRepository()
                ?? throw new GimException(GimErrorCode.ConnectionRequired, "Not connected");

            var bo = await repository.UpdateOpenChannelAsync(ChannelUrl, @params);
            var updated = OpenGroupChannelBoMapper.ToPublicModel(bo);
            if (updated != null) ApplyChannelData(updated);
            return this;
        }

        /// <summary>
        /// Deletes this open channel.
        /// </summary>
        public void Delete(GimErrorHandler handler)
        {
            _ = AsyncCallbackHelper.ExecuteVoidAsync(
                () => DeleteAsync(),
                handler,
                TAG,
                "Delete"
            );
        }

        /// <summary>
        /// Deletes this open channel (async version).
        /// </summary>
        public async Task DeleteAsync()
        {
            var repository = GIMChatMain.Instance?.GetChannelRepository()
                ?? throw new GimException(GimErrorCode.ConnectionRequired, "Not connected");

            await repository.DeleteChannelAsync(GimChannelType.Open, ChannelUrl);
            RemoveEnteredChannel(ChannelUrl);
        }

        #endregion

        #region Send File Message

        /// <summary>
        /// Sends a file message to this open channel (callback version, no progress).
        /// </summary>
        public GimFileMessage SendFileMessage(GimFileMessageCreateParams createParams, GimFileMessageHandler callback)
        {
            if (callback == null)
            {
                Logger.Warning(TAG, "SendFileMessage: callback is null");
                return null;
            }

            var pending = CreatePendingFileMessage(createParams);

            _ = AsyncCallbackHelper.ExecuteAsync(
                () => SendFileMessageCoreAsync(createParams, pending, null),
                (msg, err) => callback(msg, err),
                TAG,
                "SendFileMessage"
            );
            return pending;
        }

        /// <summary>
        /// Sends a file message to this open channel (callback version, with progress).
        /// </summary>
        public GimFileMessage SendFileMessage(GimFileMessageCreateParams createParams, IGimFileMessageWithProgressHandler handler)
        {
            if (handler == null)
            {
                Logger.Warning(TAG, "SendFileMessage: handler is null");
                return null;
            }

            var pending = CreatePendingFileMessage(createParams);

            _ = AsyncCallbackHelper.ExecuteAsync(
                () => SendFileMessageCoreAsync(createParams, pending, (reqId, bytesSent, totalBytesSent, totalBytesToSend, filePath) =>
                {
                    Internal.Platform.MainThreadDispatcher.Enqueue(() =>
                        handler.OnProgress((int)bytesSent, (int)totalBytesSent, (int)totalBytesToSend));
                }),
                (msg, err) => handler.OnResult(msg, err),
                TAG,
                "SendFileMessage"
            );
            return pending;
        }

        /// <summary>
        /// Sends a file message to this open channel (async version).
        /// </summary>
        public async Task<GimFileMessage> SendFileMessageAsync(GimFileMessageCreateParams createParams)
        {
            var pending = CreatePendingFileMessage(createParams);
            var sent = await SendFileMessageCoreAsync(createParams, pending, null);
            return sent;
        }

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
                () => ResendFileMessageCoreAsync(fileMessage, null),
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

        private GimFileMessage CreatePendingFileMessage(GimFileMessageCreateParams createParams)
        {
            var currentUser = GIMChat.CurrentUser;
            GimSender sender = null;
            if (currentUser != null)
            {
                sender = new GimSender
                {
                    UserId = currentUser.UserId,
                    Nickname = currentUser.Nickname,
                    ProfileUrl = currentUser.ProfileUrl,
                    Role = IsOperator(currentUser.UserId) ? GimRole.Operator : GimRole.None
                };
            }

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
                Sender = sender,
                FileMessageCreateParams = createParams
            };
        }

        private async Task<GimFileMessage> SendFileMessageCoreAsync(
            GimFileMessageCreateParams createParams,
            GimFileMessage pending,
            Internal.Domain.Message.FileProgressHandler progressHandler)
        {
            var repository = GIMChatMain.Instance?.GetMessageRepository()
                ?? throw new GimException(GimErrorCode.ConnectionRequired, "Not connected");
            var uploadService = GIMChatMain.Instance?.GetFileUploadService();
            var partCache = GIMChatMain.Instance?.GetFilePartCache();

            var storageRepository = GIMChatMain.Instance?.GetStorageRepository();

            var useCase = new SendFileMessageUseCase(repository, uploadService, null, partCache, storageRepository);
            var sent = await useCase.ExecuteAsync(ChannelUrl, createParams, pending, progressHandler);
            return GimFileMessage.FromBase(sent);
        }

        private async Task<GimFileMessage> ResendFileMessageCoreAsync(
            GimFileMessage fileMessage,
            Internal.Domain.Message.FileProgressHandler progressHandler)
        {
            if (fileMessage == null)
                throw new GimException(GimErrorCode.InvalidParameter, "fileMessage is null");

            var createParams = fileMessage.FileMessageCreateParams ?? new GimFileMessageCreateParams
            {
                FileUrl = fileMessage.PlainUrl,
                FileName = fileMessage.Name,
                MimeType = fileMessage.MimeType,
                FileSize = fileMessage.Size,
                CustomType = fileMessage.CustomType,
                Data = fileMessage.Data
            };

            var repository = GIMChatMain.Instance?.GetMessageRepository()
                ?? throw new GimException(GimErrorCode.ConnectionRequired, "Not connected");
            var uploadService = GIMChatMain.Instance?.GetFileUploadService();
            var partCache = GIMChatMain.Instance?.GetFilePartCache();

            var useCase = new SendFileMessageUseCase(repository, uploadService, null, partCache);
            var sent = await useCase.ExecuteAsync(ChannelUrl, createParams, fileMessage, progressHandler);
            return GimFileMessage.FromBase(sent);
        }

        #endregion


        #region OpenChannel-Specific Queries

        /// <summary>
        /// Creates a query to fetch participants in this open channel.
        /// </summary>
        /// <param name="limit">Max number of participants per page (default 20).</param>
        /// <returns>A query for fetching participants.</returns>
        public GimParticipantListQuery CreateParticipantListQuery(int limit = 20)
        {
            return new GimParticipantListQuery(ChannelUrl, limit);
        }

        #endregion

        #region Static Entered Channels Management

        private static readonly ConcurrentDictionary<string, GimOpenChannel> _enteredChannels
            = new ConcurrentDictionary<string, GimOpenChannel>();

        internal static void AddEnteredChannel(GimOpenChannel channel)
        {
            if (channel == null || string.IsNullOrEmpty(channel.ChannelUrl)) return;
            _enteredChannels[channel.ChannelUrl] = channel;
        }

        internal static void RemoveEnteredChannel(string channelUrl)
        {
            if (string.IsNullOrEmpty(channelUrl)) return;
            _enteredChannels.TryRemove(channelUrl, out _);
        }

        public static bool IsEnteredChannel(string channelUrl)
        {
            if (string.IsNullOrEmpty(channelUrl)) return false;
            return _enteredChannels.ContainsKey(channelUrl);
        }

        public static GimOpenChannel GetEnteredChannel(string channelUrl)
        {
            if (string.IsNullOrEmpty(channelUrl)) return null;
            return _enteredChannels.TryGetValue(channelUrl, out var channel) ? channel : null;
        }

        internal static void ClearEnteredChannels()
        {
            _enteredChannels.Clear();
        }

        internal static async Task TryToEnterEnteredOpenChannelsAsync(Func<GimOpenChannel, Task> enterFunc = null)
        {
            var channels = _enteredChannels.Values.ToList();
            if (!channels.Any()) return;

            enterFunc ??= ch => ch.EnterAsync();

            Logger.Info(TAG, $"Try to re-enter {channels.Count} open channels...");

            var failedUrls = new List<string>();

            foreach (var channel in channels)
            {
                try
                {
                    await enterFunc(channel);
                }
                catch (Exception ex)
                {
                    Logger.Warning(TAG, $"Failed to re-enter {channel.ChannelUrl}: {ex.Message}");
                    failedUrls.Add(channel.ChannelUrl);
                }
            }

            foreach (var url in failedUrls)
            {
                RemoveEnteredChannel(url);
            }
        }

        #endregion

        #region Handler Management

        private static readonly ConcurrentDictionary<string, IGimOpenChannelHandler> _handlers = new();

        /// <summary>
        /// Adds an open channel handler with the specified identifier.
        /// </summary>
        public static void AddOpenChannelHandler(string identifier, IGimOpenChannelHandler handler)
        {
            if (string.IsNullOrEmpty(identifier) || handler == null) return;
            _handlers[identifier] = handler;
        }

        /// <summary>
        /// Removes the open channel handler with the specified identifier.
        /// </summary>
        public static void RemoveOpenChannelHandler(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return;
            _handlers.TryRemove(identifier, out _);
        }

        /// <summary>
        /// Removes all registered handlers.
        /// </summary>
        internal static void ClearAllHandlers()
        {
            _handlers.Clear();
        }

        internal static IEnumerable<IGimOpenChannelHandler> GetHandlers()
        {
            return _handlers.Values.ToList();
        }

        #endregion

        #region Internal Event Triggers

        internal static void TriggerUserEntered(GimOpenChannel channel, GimUser user)
        {
            if (channel?.ChannelUrl == null || !IsEnteredChannel(channel.ChannelUrl)) return;

            foreach (var handler in GetHandlers())
            {
                try { handler.OnUserEntered(channel, user); }
                catch (Exception e) { Logger.Error(TAG, "Error in OnUserEntered handler", e); }
            }
        }

        internal static void TriggerUserExited(GimOpenChannel channel, GimUser user)
        {
            if (channel?.ChannelUrl == null || !IsEnteredChannel(channel.ChannelUrl)) return;

            foreach (var handler in GetHandlers())
            {
                try { handler.OnUserExited(channel, user); }
                catch (Exception e) { Logger.Error(TAG, "Error in OnUserExited handler", e); }
            }
        }

        internal static void TriggerChannelUpdated(GimOpenChannel channel)
        {
            if (channel?.ChannelUrl == null || !IsEnteredChannel(channel.ChannelUrl)) return;

            foreach (var handler in GetHandlers())
            {
                try { handler.OnChannelUpdated(channel); }
                catch (Exception e) { Logger.Error(TAG, "Error in OnChannelUpdated handler", e); }
            }
        }

        internal static void TriggerChannelDeleted(string channelUrl)
        {
            if (channelUrl == null || !IsEnteredChannel(channelUrl)) return;

            RemoveEnteredChannel(channelUrl);

            foreach (var handler in GetHandlers())
            {
                try { handler.OnChannelDeleted(channelUrl); }
                catch (Exception e) { Logger.Error(TAG, "Error in OnChannelDeleted handler", e); }
            }
        }

        internal static void TriggerMessageReceived(GimOpenChannel channel, GimBaseMessage message)
        {
            if (channel?.ChannelUrl == null || !IsEnteredChannel(channel.ChannelUrl)) return;

            foreach (var handler in GetHandlers())
            {
                try { handler.OnMessageReceived(channel, message); }
                catch (Exception e) { Logger.Error(TAG, "Error in OnMessageReceived handler", e); }
            }
        }

        internal static void TriggerChannelParticipantCountChanged(IReadOnlyList<GimOpenChannel> channels)
        {
            var entered = channels.Where(c => c?.ChannelUrl != null && IsEnteredChannel(c.ChannelUrl)).ToList();
            if (entered.Count == 0) return;

            foreach (var handler in GetHandlers())
            {
                try { handler.OnChannelParticipantCountChanged(entered); }
                catch (Exception e) { Logger.Error(TAG, "Error in OnChannelParticipantCountChanged handler", e); }
            }
        }

        internal static void TriggerChannelFrozen(GimOpenChannel channel)
        {
            if (channel?.ChannelUrl == null || !IsEnteredChannel(channel.ChannelUrl)) return;

            foreach (var handler in GetHandlers())
            {
                try { handler.OnChannelFrozen(channel); }
                catch (Exception e) { Logger.Error(TAG, "Error in OnChannelFrozen handler", e); }
            }
        }

        internal static void TriggerChannelUnfrozen(GimOpenChannel channel)
        {
            if (channel?.ChannelUrl == null || !IsEnteredChannel(channel.ChannelUrl)) return;

            foreach (var handler in GetHandlers())
            {
                try { handler.OnChannelUnfrozen(channel); }
                catch (Exception e) { Logger.Error(TAG, "Error in OnChannelUnfrozen handler", e); }
            }
        }

        internal static void TriggerOperatorUpdated(GimOpenChannel channel)
        {
            if (channel?.ChannelUrl == null || !IsEnteredChannel(channel.ChannelUrl)) return;

            foreach (var handler in GetHandlers())
            {
                try { handler.OnOperatorUpdated(channel); }
                catch (Exception e) { Logger.Error(TAG, "Error in OnOperatorUpdated handler", e); }
            }
        }

        internal static void TriggerUserMuted(GimOpenChannel channel, GimUser user)
        {
            if (channel?.ChannelUrl == null || !IsEnteredChannel(channel.ChannelUrl)) return;

            foreach (var handler in GetHandlers())
            {
                try { handler.OnUserMuted(channel, user); }
                catch (Exception e) { Logger.Error(TAG, "Error in OnUserMuted handler", e); }
            }
        }

        internal static void TriggerUserUnmuted(GimOpenChannel channel, GimUser user)
        {
            if (channel?.ChannelUrl == null || !IsEnteredChannel(channel.ChannelUrl)) return;

            foreach (var handler in GetHandlers())
            {
                try { handler.OnUserUnmuted(channel, user); }
                catch (Exception e) { Logger.Error(TAG, "Error in OnUserUnmuted handler", e); }
            }
        }

        internal static void TriggerUserBanned(GimOpenChannel channel, GimUser user)
        {
            if (channel?.ChannelUrl == null || !IsEnteredChannel(channel.ChannelUrl)) return;

            if (user?.UserId != null && user.UserId == GIMChat.CurrentUser?.UserId)
            {
                RemoveEnteredChannel(channel.ChannelUrl);
            }

            foreach (var handler in GetHandlers())
            {
                try { handler.OnUserBanned(channel, user); }
                catch (Exception e) { Logger.Error(TAG, "Error in OnUserBanned handler", e); }
            }
        }

        internal static void TriggerUserUnbanned(GimOpenChannel channel, GimUser user)
        {
            if (channel?.ChannelUrl == null || !IsEnteredChannel(channel.ChannelUrl)) return;

            foreach (var handler in GetHandlers())
            {
                try { handler.OnUserUnbanned(channel, user); }
                catch (Exception e) { Logger.Error(TAG, "Error in OnUserUnbanned handler", e); }
            }
        }

        internal static void TriggerMessageUpdated(GimOpenChannel channel, GimBaseMessage message)
        {
            if (channel?.ChannelUrl == null || !IsEnteredChannel(channel.ChannelUrl)) return;

            foreach (var handler in GetHandlers())
            {
                try { handler.OnMessageUpdated(channel, message); }
                catch (Exception e) { Logger.Error(TAG, "Error in OnMessageUpdated handler", e); }
            }
        }

        internal static void TriggerMessageDeleted(GimOpenChannel channel, long messageId)
        {
            if (channel?.ChannelUrl == null || !IsEnteredChannel(channel.ChannelUrl)) return;

            foreach (var handler in GetHandlers())
            {
                try { handler.OnMessageDeleted(channel, messageId); }
                catch (Exception e) { Logger.Error(TAG, "Error in OnMessageDeleted handler", e); }
            }
        }

        #endregion

        #region Static Channel Operations

        public static Task<GimOpenChannel> GetChannelAsync(string channelUrl)
            => GetChannelCoreAsync(GimChannelType.Open, channelUrl,
                bo => OpenGroupChannelBoMapper.ToPublicModel((OpenChannelBO)bo));

        public static void GetChannel(string channelUrl, Action<GimOpenChannel, GimException> callback)
            => GetChannelCore(GimChannelType.Open, channelUrl,
                bo => OpenGroupChannelBoMapper.ToPublicModel((OpenChannelBO)bo),
                (ch, err) => callback?.Invoke(ch, err),
                TAG, "GetChannel");

        public static async Task<GimOpenChannel> CreateChannelAsync(GimOpenChannelCreateParams @params)
        {
            var repository = GIMChatMain.Instance?.GetChannelRepository()
                ?? throw new GimException(GimErrorCode.ConnectionRequired, "Not connected");
            var bo = await repository.CreateOpenChannelAsync(@params);
            return OpenGroupChannelBoMapper.ToPublicModel(bo);
        }

        public static void CreateChannel(GimOpenChannelCreateParams @params, Action<GimOpenChannel, GimException> callback)
        {
            if (callback == null)
            {
                Logger.Warning(TAG, "CreateChannel: callback is null");
                return;
            }

            _ = AsyncCallbackHelper.ExecuteAsync(
                () => CreateChannelAsync(@params),
                (ch, err) => callback(ch, err),
                TAG,
                "CreateChannel"
            );
        }

        public static GimOpenChannelListQuery CreateOpenChannelListQuery(GimOpenChannelListQueryParams @params = null)
        {
            return new GimOpenChannelListQuery(@params ?? new GimOpenChannelListQueryParams());
        }

        #endregion

        // Testing helpers
        internal static int EnteredChannelCount => _enteredChannels.Count;
    }
}
