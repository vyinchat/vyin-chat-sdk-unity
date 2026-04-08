// Runtime/Public/Collection/GimMessageCollection.cs
// Message collection: Manages paginated message list for a single channel with real-time updates.
// Lifecycle: Constructor → StartCollection → LoadPrevious/LoadNext → Dispose
// Unity SDK: No local cache, API-only loading.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Domain.Collections;
using Gamania.GIMChat.Internal.Domain.Log;
using Gamania.GIMChat.Internal.Domain.Mappers;
using Gamania.GIMChat.Internal.Platform;
using Gamania.GIMChat.Internal.Platform.Unity;
using Logger = Gamania.GIMChat.Internal.Domain.Log.Logger;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Message collection: Manages paginated, real-time updated message list for a single channel.
    /// Lifecycle: Constructor → StartCollection → LoadPrevious/LoadNext → Dispose.
    ///
    /// Unity SDK simplified design: No local cache, API-only loading.
    /// </summary>
    public sealed class GimMessageCollection : GimBaseCollection, IDisposable
    {
        // ══════════════════════════════════════════════════════════════════════════════
        // Private Fields
        // ══════════════════════════════════════════════════════════════════════════════

        private bool _isDisposed;
        private bool _isLoading;
        private bool _isStarted;

        /// <summary>In-memory sorted message list.</summary>
        private readonly SortedMessageList _cachedMessages = new();

        /// <summary>Unique identifier for event handler registration.</summary>
        private readonly string _identifier;

        /// <summary>Semaphore for serializing changelog operations.</summary>
        private readonly SemaphoreSlim _changelogLock = new(1, 1);

        /// <summary>Timestamp tracking for pagination.</summary>
        private long _oldestSyncedTs;
        private long _latestSyncedTs;

        /// <summary>Message list parameters (page size, filters, etc.).</summary>
        private readonly GimMessageListParams _messageListParams;

        // ══════════════════════════════════════════════════════════════════════════════
        // Public Properties
        // ══════════════════════════════════════════════════════════════════════════════

        /// <summary>The channel this collection is bound to.</summary>
        public GimGroupChannel Channel { get; }

        /// <summary>Starting timestamp for initial message loading (Unix ms). long.MaxValue = latest, 0 = oldest.</summary>
        public long StartingPoint { get; }

        /// <summary>Whether older messages can be loaded via LoadPrevious.</summary>
        public bool HasPrevious { get; private set; } = true;

        /// <summary>Whether newer messages can be loaded via LoadNext.</summary>
        public bool HasNext { get; private set; } = true;

        /// <summary>True after Dispose() is called.</summary>
        public bool IsDisposed => _isDisposed;

        /// <summary>
        /// Successfully sent messages (server confirmed), sorted by time.
        /// </summary>
        public IReadOnlyList<GimBaseMessage> SucceededMessages =>
            _cachedMessages.ToReadOnlyList();

        /// <summary>
        /// Pending messages for this channel (local, awaiting server ACK).
        /// </summary>
        public IReadOnlyList<GimBaseMessage> PendingMessages =>
            DataSource.GetPendingMessages(Channel?.ChannelUrl ?? "");

        /// <summary>
        /// Failed messages for this channel.
        /// </summary>
        public IReadOnlyList<GimBaseMessage> FailedMessages =>
            DataSource.GetFailedMessages(Channel?.ChannelUrl ?? "");

        /// <summary>
        /// Delegate for receiving collection events (message add/update/delete, channel changes).
        /// </summary>
        public IGimMessageCollectionDelegate Delegate { get; set; }

        /// <summary>
        /// Message list parameters (page size, filters, etc.).
        /// </summary>
        public GimMessageListParams MessageListParams => _messageListParams;

        // ══════════════════════════════════════════════════════════════════════════════
        // Constructor
        // ══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates a MessageCollection for the specified channel.
        /// Call StartCollection() after construction to begin loading data.
        /// </summary>
        /// <param name="channel">The channel to load messages from.</param>
        /// <param name="startingPoint">Starting timestamp (Unix ms); long.MaxValue (default) = latest, 0 = oldest.</param>
        public GimMessageCollection(GimGroupChannel channel, long startingPoint = long.MaxValue)
            : this(channel, new GimMessageCollectionCreateParams
            {
                StartingPoint = startingPoint,
                MessageListParams = new GimMessageListParams()
            })
        {
        }

        /// <summary>
        /// Creates a MessageCollection for the specified channel using CreateParams.
        /// Call StartCollection() after construction to begin loading data.
        /// </summary>
        /// <param name="channel">The channel to load messages from.</param>
        /// <param name="createParams">Creation parameters including starting point and message list params.</param>
        public GimMessageCollection(GimGroupChannel channel, GimMessageCollectionCreateParams createParams)
        {
            Channel = channel ?? throw new ArgumentNullException(nameof(channel));
            createParams ??= new GimMessageCollectionCreateParams();

            StartingPoint = createParams.StartingPoint;
            _messageListParams = createParams.MessageListParams ?? new GimMessageListParams();

            // Initialize timestamp tracking
            _oldestSyncedTs = long.MaxValue;
            _latestSyncedTs = 0;

            // Generate unique identifier
            _identifier = $"mc_{Guid.NewGuid():N}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

            // Register event handlers
            StartObserve();
        }

        /// <summary>
        /// Registers channel and connection event handlers.
        /// </summary>
        private void StartObserve()
        {
            DelegateManager.AddChannelHandler(_identifier, new GimGroupChannelHandler
            {
                OnMessageReceived = HandleMessageReceived,
                OnMessageUpdated = HandleMessageUpdated,
                OnMessageDeleted = HandleMessageDeleted,
                OnChannelChanged = HandleChannelChanged,
                OnChannelDeleted = HandleChannelDeleted
            });

            // Subscribe to internal message sending events
            GimGroupChannel.InternalMessagePending += HandleMessagePending;
            GimGroupChannel.InternalMessageSent += HandleMessageSent;
            GimGroupChannel.InternalMessageFailed += HandleMessageFailed;

            GIMChatMain.Instance.AddConnectionHandler(_identifier, new GimConnectionHandler
            {
                OnReconnectSucceeded = HandleReconnectSucceeded
            });
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // Lifecycle
        // ══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Releases resources and unregisters channel event handlers.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            // Unregister event handlers
            DelegateManager.RemoveChannelHandler(_identifier);
            GIMChatMain.Instance.RemoveConnectionHandler(_identifier);

            // Unsubscribe from internal message sending events
            GimGroupChannel.InternalMessagePending -= HandleMessagePending;
            GimGroupChannel.InternalMessageSent -= HandleMessageSent;
            GimGroupChannel.InternalMessageFailed -= HandleMessageFailed;

            // Clear cached messages
            _cachedMessages.Clear();

            // Release changelog lock
            _changelogLock?.Dispose();
        }

        /// <summary>Checks if disposed and throws exception if true.</summary>
        private void GuardDisposed()
        {
            if (_isDisposed)
                throw new GimException(
                    GimErrorCode.CollectionDisposed,
                    "GimMessageCollection has been disposed. Create a new instance.");
        }

        /// <summary>Checks if StartCollection was called and throws exception if not.</summary>
        private void GuardNotStarted()
        {
            if (!_isStarted)
                throw new GimException(
                    GimErrorCode.CollectionNotStarted,
                    "StartCollection() must be called before this operation.");
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // Event Handlers
        // ══════════════════════════════════════════════════════════════════════════════

        /// <summary>Handles new message received event.</summary>
        private void HandleMessageReceived(GimGroupChannel channel, GimBaseMessage message)
        {
            if (_isDisposed || channel?.ChannelUrl != Channel.ChannelUrl) return;

            // If not loaded to latest (hasNext == true), don't add new messages
            if (HasNext)
            {
                // Message outside loaded range; notify update if already exists
                if (_cachedMessages.Contains(message.MessageId))
                {
                    DispatchToMainThread(() =>
                    {
                        if (_isDisposed) return;
                        var context = new GimMessageContext(GimCollectionEventSource.EventMessageReceived, GimMessageSendingStatus.Succeeded);
                        Delegate?.OnMessagesUpdated(this, context, Channel, new[] { message });
                    });
                }
                return;
            }

            // Check if message already exists (streaming update scenario)
            bool isUpdate = _cachedMessages.Contains(message.MessageId);
            Logger.Debug(LogCategory.Message,
                $"[MessageCollection] HandleMessageReceived: msgId={message.MessageId}, isUpdate={isUpdate}, cacheCount={_cachedMessages.Count}, Done={message.Done}");

            // Add or update cached message
            _cachedMessages.Insert(message);
            _latestSyncedTs = Math.Max(_latestSyncedTs, message.CreatedAt);

            DispatchToMainThread(() =>
            {
                if (_isDisposed) return;
                var context = new GimMessageContext(GimCollectionEventSource.EventMessageReceived, GimMessageSendingStatus.Succeeded);
                if (isUpdate)
                {
                    Logger.Debug(LogCategory.Message, $"[MessageCollection] -> OnMessagesUpdated (streaming update)");
                    Delegate?.OnMessagesUpdated(this, context, Channel, new[] { message });
                }
                else
                {
                    Logger.Debug(LogCategory.Message, $"[MessageCollection] -> OnMessagesAdded (new message)");
                    Delegate?.OnMessagesAdded(this, context, Channel, new[] { message });
                }
            });
        }

        /// <summary>Handles message updated event.</summary>
        private void HandleMessageUpdated(GimGroupChannel channel, GimBaseMessage message)
        {
            if (_isDisposed || channel?.ChannelUrl != Channel.ChannelUrl) return;
            if (!_cachedMessages.Contains(message.MessageId)) return;

            _cachedMessages.Insert(message); // Replace existing

            DispatchToMainThread(() =>
            {
                if (_isDisposed) return;
                var context = new GimMessageContext(GimCollectionEventSource.EventMessageUpdated, GimMessageSendingStatus.Succeeded);
                Delegate?.OnMessagesUpdated(this, context, Channel, new[] { message });
            });
        }

        /// <summary>Handles message deleted event.</summary>
        private void HandleMessageDeleted(GimGroupChannel channel, long messageId)
        {
            if (_isDisposed || channel?.ChannelUrl != Channel.ChannelUrl) return;

            if (!_cachedMessages.TryGet(messageId, out var message)) return;

            _cachedMessages.Remove(messageId);

            DispatchToMainThread(() =>
            {
                if (_isDisposed) return;
                var context = new GimMessageContext(GimCollectionEventSource.EventMessageDeleted, GimMessageSendingStatus.Succeeded);
                Delegate?.OnMessagesDeleted(this, context, Channel, new[] { message });
            });
        }

        /// <summary>Handles channel changed event.</summary>
        private void HandleChannelChanged(GimGroupChannel channel)
        {
            if (_isDisposed || channel?.ChannelUrl != Channel.ChannelUrl) return;

            DispatchToMainThread(() =>
            {
                if (_isDisposed) return;
                var context = new GimMessageContext(GimCollectionEventSource.EventChannelChanged, GimMessageSendingStatus.None);
                Delegate?.OnChannelUpdated(this, context, channel);
            });
        }

        /// <summary>Handles channel deleted event.</summary>
        private void HandleChannelDeleted(string channelUrl)
        {
            if (_isDisposed || channelUrl != Channel.ChannelUrl) return;

            DispatchToMainThread(() =>
            {
                if (_isDisposed) return;
                var context = new GimMessageContext(GimCollectionEventSource.EventChannelDeleted, GimMessageSendingStatus.None);
                Delegate?.OnChannelDeleted(this, context, channelUrl);
            });
        }

        /// <summary>Handles reconnection succeeded event.</summary>
        private void HandleReconnectSucceeded()
        {
            if (_isDisposed || !_isStarted) return;

            Logger.Info(LogCategory.Message, "Reconnected, requesting message changelogs");
            _ = RequestChangeLogsAsync();
            _ = CheckHugeGapAsync();
        }

        /// <summary>Handles local pending message creation event.</summary>
        private void HandleMessagePending(GimGroupChannel channel, GimBaseMessage message)
        {
            if (_isDisposed || channel?.ChannelUrl != Channel.ChannelUrl) return;

            // Add pending message to DataSource
            DataSource.AddPendingMessage(message);

            DispatchToMainThread(() =>
            {
                if (_isDisposed) return;
                var context = new GimMessageContext(GimCollectionEventSource.LocalMessagePendingCreated, GimMessageSendingStatus.Pending);
                Delegate?.OnMessagesAdded(this, context, Channel, new[] { message });
            });
        }

        /// <summary>Handles message sent successfully event (server ACK).</summary>
        private void HandleMessageSent(GimGroupChannel channel, GimBaseMessage message)
        {
            if (_isDisposed || channel?.ChannelUrl != Channel.ChannelUrl) return;

            // Remove from pending (using original pending message's ReqId)
            DataSource.RemovePendingMessage(message.ReqId, message.ChannelUrl);

            // Add confirmed message to cache
            _cachedMessages.Insert(message);
            _latestSyncedTs = Math.Max(_latestSyncedTs, message.CreatedAt);

            DispatchToMainThread(() =>
            {
                if (_isDisposed) return;
                var context = new GimMessageContext(GimCollectionEventSource.EventMessageSent, GimMessageSendingStatus.Succeeded);
                Delegate?.OnMessagesUpdated(this, context, Channel, new[] { message });
            });
        }

        /// <summary>Handles message send failed event.</summary>
        private void HandleMessageFailed(GimGroupChannel channel, GimBaseMessage message, GimException error)
        {
            if (_isDisposed || channel?.ChannelUrl != Channel.ChannelUrl) return;

            // Remove from pending
            DataSource.RemovePendingMessage(message.ReqId, message.ChannelUrl);

            // Update message status
            message.SendingStatus = GimSendingStatus.Failed;
            message.ErrorCode = error?.ErrorCode;

            // Add failed message to DataSource
            DataSource.AddFailedMessage(message);

            DispatchToMainThread(() =>
            {
                if (_isDisposed) return;
                var context = new GimMessageContext(GimCollectionEventSource.LocalMessageFailed, GimMessageSendingStatus.Failed);
                Delegate?.OnMessagesUpdated(this, context, Channel, new[] { message });
            });
        }

        /// <summary>Dispatches action to main thread for execution.</summary>
        private void DispatchToMainThread(Action action)
        {
            MainThreadDispatcher.Enqueue(action);
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // Callback API
        // ══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Initializes collection by loading messages from API (Unity SDK: no local cache).
        /// </summary>
        /// <param name="completionHandler">Callback invoked after loading with message list or error.</param>
        public void StartCollection(GimMessageListHandler completionHandler)
        {
            GuardDisposed();
            if (_isStarted)
            {
                completionHandler?.Invoke(null, new GimException(GimErrorCode.InvalidOperation, "Collection already started."));
                return;
            }

            _ = StartCollectionCoreAsync(completionHandler);
        }

        /// <summary>Loads older messages (backward pagination).</summary>
        public void LoadPrevious(GimMessageListHandler completionHandler)
        {
            GuardDisposed();
            GuardNotStarted();
            _ = AsyncCallbackHelper.ExecuteAsync(
                () => LoadPreviousCoreAsync(),
                (messages, error) => completionHandler?.Invoke(messages, error),
                "GimMessageCollection.LoadPrevious",
                "LoadPrevious"
            );
        }

        /// <summary>Loads newer messages (forward pagination).</summary>
        public void LoadNext(GimMessageListHandler completionHandler)
        {
            GuardDisposed();
            GuardNotStarted();
            _ = AsyncCallbackHelper.ExecuteAsync(
                () => LoadNextCoreAsync(),
                (messages, error) => completionHandler?.Invoke(messages, error),
                "GimMessageCollection.LoadNext",
                "LoadNext"
            );
        }

        /// <summary>Removes specified failed messages from DataSource.</summary>
        public void RemoveFailed(IList<GimBaseMessage> messages, GimErrorHandler completionHandler = null)
        {
            GuardDisposed();
            _ = RemoveFailedCoreAsync(messages, completionHandler);
        }

        /// <summary>Removes all failed messages from DataSource.</summary>
        public void RemoveAllFailed(GimErrorHandler completionHandler = null)
        {
            GuardDisposed();
            _ = RemoveAllFailedCoreAsync(completionHandler);
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // Async API
        // ══════════════════════════════════════════════════════════════════════════════

        /// <summary>Asynchronously loads older messages.</summary>
        public async Task<IReadOnlyList<GimBaseMessage>> LoadPreviousAsync()
        {
            GuardDisposed();
            GuardNotStarted();
            return await LoadPreviousCoreAsync();
        }

        /// <summary>Asynchronously loads newer messages.</summary>
        public async Task<IReadOnlyList<GimBaseMessage>> LoadNextAsync()
        {
            GuardDisposed();
            GuardNotStarted();
            return await LoadNextCoreAsync();
        }

        /// <summary>Asynchronously removes specified failed messages.</summary>
        public async Task RemoveFailedAsync(IList<GimBaseMessage> messages)
        {
            GuardDisposed();
            await RemoveFailedCoreAsync(messages, null);
        }

        /// <summary>Asynchronously removes all failed messages.</summary>
        public async Task RemoveAllFailedAsync()
        {
            GuardDisposed();
            await RemoveAllFailedCoreAsync(null);
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // Core Implementation
        // ══════════════════════════════════════════════════════════════════════════════

        /// <summary>Core async implementation of StartCollection.</summary>
        private async Task StartCollectionCoreAsync(GimMessageListHandler completionHandler)
        {
            _isStarted = true;
            _isLoading = true;

            try
            {
                long ts = StartingPoint;
                var repo = GIMChatMain.Instance.GetMessageRepository();

                var result = await repo.GetMessagesAsync(
                    Channel.ChannelUrl,
                    messageTs: ts,
                    messageListParams: _messageListParams);

                var messages = new List<GimBaseMessage>();
                foreach (var bo in result.Messages)
                {
                    messages.Add(MessageBoMapper.ToPublicModel(bo));
                }

                // Insert cached messages
                _cachedMessages.InsertAllIfNotExist(messages);

                // Update timestamps
                if (_cachedMessages.Count > 0)
                {
                    _oldestSyncedTs = _cachedMessages.OldestMessage.CreatedAt;
                    _latestSyncedTs = _cachedMessages.LatestMessage.CreatedAt;
                }

                // Use API returned hasNext/hasPrevious
                HasPrevious = result.HasPrevious;
                HasNext = StartingPoint != long.MaxValue && result.HasNext;

                Logger.Info(LogCategory.Message, $"StartCollection completed: {messages.Count} messages, HasPrevious={HasPrevious}, HasNext={HasNext}");

                DispatchToMainThread(() =>
                {
                    if (_isDisposed) return;
                    completionHandler?.Invoke(messages.AsReadOnly(), null);
                });

                // Auto-trigger changelog sync
                _ = RequestChangeLogsAsync();
            }
            catch (Exception ex)
            {
                var error = ex as GimException ?? new GimException(GimErrorCode.UnknownError, ex.Message);
                Logger.Error(LogCategory.Message, $"StartCollection failed: {error.Message}");

                DispatchToMainThread(() =>
                {
                    if (_isDisposed) return;
                    completionHandler?.Invoke(null, error);
                });
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>Core async implementation of LoadPrevious.</summary>
        private async Task<IReadOnlyList<GimBaseMessage>> LoadPreviousCoreAsync()
        {
            if (!HasPrevious)
            {
                Logger.Info(LogCategory.Message, "No more previous messages.");
                return new List<GimBaseMessage>().AsReadOnly();
            }

            if (_isLoading)
            {
                Logger.Info(LogCategory.Message, "LoadPrevious already in progress.");
                return new List<GimBaseMessage>().AsReadOnly();
            }

            if (_cachedMessages.Count == 0)
            {
                Logger.Warning(LogCategory.Message, "LoadPrevious: no cached messages to paginate from.");
                return new List<GimBaseMessage>().AsReadOnly();
            }

            _isLoading = true;
            try
            {
                var oldestMessage = _cachedMessages.OldestMessage;

                var repo = GIMChatMain.Instance.GetMessageRepository();

                // Create params for backward pagination
                var prevParams = _messageListParams.Clone();
                prevParams.NextResultSize = 0;

                var result = await repo.GetMessagesAsync(
                    Channel.ChannelUrl,
                    messageTs: oldestMessage.CreatedAt,
                    messageListParams: prevParams);

                var messages = new List<GimBaseMessage>();
                foreach (var bo in result.Messages)
                {
                    messages.Add(MessageBoMapper.ToPublicModel(bo));
                }

                // Insert cached messages (deduplicate)
                var inserted = _cachedMessages.InsertAllIfNotExist(messages);

                // Use API returned hasPrevious
                HasPrevious = result.HasPrevious;

                // Update oldestSyncedTs
                if (_cachedMessages.Count > 0)
                {
                    _oldestSyncedTs = Math.Min(_oldestSyncedTs, _cachedMessages.OldestMessage.CreatedAt);
                }

                Logger.Info(LogCategory.Message, $"LoadPrevious completed: {inserted.Count} new messages, HasPrevious={HasPrevious}");
                return inserted;
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>Core async implementation of LoadNext.</summary>
        private async Task<IReadOnlyList<GimBaseMessage>> LoadNextCoreAsync()
        {
            if (!HasNext)
            {
                Logger.Info(LogCategory.Message, "No more next messages.");
                return new List<GimBaseMessage>().AsReadOnly();
            }

            if (_isLoading)
            {
                Logger.Info(LogCategory.Message, "LoadNext already in progress.");
                return new List<GimBaseMessage>().AsReadOnly();
            }

            if (_cachedMessages.Count == 0)
            {
                Logger.Warning(LogCategory.Message, "LoadNext: no cached messages to paginate from.");
                return new List<GimBaseMessage>().AsReadOnly();
            }

            _isLoading = true;
            try
            {
                var latestMessage = _cachedMessages.LatestMessage;

                var repo = GIMChatMain.Instance.GetMessageRepository();

                // Create params for forward pagination
                var nextParams = _messageListParams.Clone();
                nextParams.PreviousResultSize = 0;
                nextParams.NextResultSize = _messageListParams.PreviousResultSize; // Use same page size

                var result = await repo.GetMessagesAsync(
                    Channel.ChannelUrl,
                    messageTs: latestMessage.CreatedAt,
                    messageListParams: nextParams);

                var messages = new List<GimBaseMessage>();
                foreach (var bo in result.Messages)
                {
                    messages.Add(MessageBoMapper.ToPublicModel(bo));
                }

                // Insert cached messages (deduplicate)
                var inserted = _cachedMessages.InsertAllIfNotExist(messages);

                // Use API returned hasNext
                HasNext = result.HasNext;

                // Update latestSyncedTs
                if (_cachedMessages.Count > 0)
                {
                    _latestSyncedTs = Math.Max(_latestSyncedTs, _cachedMessages.LatestMessage.CreatedAt);
                }

                Logger.Info(LogCategory.Message, $"LoadNext completed: {inserted.Count} new messages, HasNext={HasNext}");
                return inserted;
            }
            finally
            {
                _isLoading = false;
            }
        }

        /// <summary>Core async implementation of RemoveFailed.</summary>
        private async Task RemoveFailedCoreAsync(IList<GimBaseMessage> messages, GimErrorHandler completionHandler)
        {
            try
            {
                if (messages == null || messages.Count == 0)
                {
                    completionHandler?.Invoke(null);
                    return;
                }

                DataSource.DeleteFailedMessages(messages, Channel.ChannelUrl);
                Logger.Info(LogCategory.Message, $"Removed {messages.Count} failed messages");

                await Task.CompletedTask;
                completionHandler?.Invoke(null);
            }
            catch (Exception ex)
            {
                var error = ex as GimException ?? new GimException(GimErrorCode.UnknownError, ex.Message);
                completionHandler?.Invoke(error);
            }
        }

        /// <summary>Core async implementation of RemoveAllFailed.</summary>
        private async Task RemoveAllFailedCoreAsync(GimErrorHandler completionHandler)
        {
            try
            {
                DataSource.DeleteAllFailedMessages(Channel.ChannelUrl);
                Logger.Info(LogCategory.Message, "Removed all failed messages");

                await Task.CompletedTask;
                completionHandler?.Invoke(null);
            }
            catch (Exception ex)
            {
                var error = ex as GimException ?? new GimException(GimErrorCode.UnknownError, ex.Message);
                completionHandler?.Invoke(error);
            }
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // Changelog and Huge Gap
        // ══════════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Requests changelog to sync messages missed during offline period.
        /// </summary>
        internal async Task<bool> RequestChangeLogsAsync()
        {
            if (_isDisposed || !_isStarted) return false;

            if (!await _changelogLock.WaitAsync(0))
            {
                Logger.Info(LogCategory.Message, "Changelog already in progress");
                return false;
            }

            try
            {
                var repo = GIMChatMain.Instance.GetMessageRepository();
                var result = await repo.GetMessageChangeLogsAsync(Channel.ChannelUrl, _latestSyncedTs);

                var updatedMessages = new List<GimBaseMessage>();
                foreach (var bo in result.UpdatedMessages)
                {
                    var msg = MessageBoMapper.ToPublicModel(bo);
                    updatedMessages.Add(msg);
                    _cachedMessages.Insert(msg); // Replace existing
                    _latestSyncedTs = Math.Max(_latestSyncedTs, msg.CreatedAt);
                }

                var deletedMessages = new List<GimBaseMessage>();
                foreach (var id in result.DeletedMessageIds)
                {
                    if (_cachedMessages.TryGet(id, out var msg))
                    {
                        deletedMessages.Add(msg);
                        _cachedMessages.Remove(id);
                    }
                }

                DispatchToMainThread(() =>
                {
                    if (_isDisposed) return;

                    if (updatedMessages.Count > 0)
                    {
                        var updateContext = new GimMessageContext(GimCollectionEventSource.MessageChangelog, GimMessageSendingStatus.Succeeded);
                        Delegate?.OnMessagesUpdated(this, updateContext, Channel, updatedMessages);
                    }

                    if (deletedMessages.Count > 0)
                    {
                        var deleteContext = new GimMessageContext(GimCollectionEventSource.MessageChangelog, GimMessageSendingStatus.Succeeded);
                        Delegate?.OnMessagesDeleted(this, deleteContext, Channel, deletedMessages);
                    }
                });

                Logger.Info(LogCategory.Message, "Changelog sync completed");
                return true;
            }
            catch (GimException ex)
            {
                Logger.Error(LogCategory.Message, $"Changelog failed: {ex.Message}");
                return false;
            }
            finally
            {
                _changelogLock.Release();
            }
        }

        /// <summary>
        /// Checks if there's a huge gap in message timeline after reconnection.
        /// If huge gap detected, triggers OnHugeGapDetected to let UI decide whether to reset;
        /// if small gap, automatically fills in missing messages.
        /// </summary>
        public async Task CheckHugeGapAsync()
        {
            if (_isDisposed || !_isStarted || _cachedMessages.Count == 0) return;

            try
            {
                var repo = GIMChatMain.Instance.GetMessageRepository();

                long cachedOldestTs = _cachedMessages.OldestMessage.CreatedAt;
                long cachedLatestTs = HasNext ? _cachedMessages.LatestMessage.CreatedAt : long.MaxValue;

                // If no sync needed (oldest and latest times are within sync range), return directly
                if (HasNext && _oldestSyncedTs <= cachedOldestTs && cachedLatestTs <= _latestSyncedTs)
                {
                    return;
                }

                int prevCacheCount = _cachedMessages.GetCountBefore(_oldestSyncedTs, inclusive: true);
                int nextCacheCount = _cachedMessages.GetCountAfter(_latestSyncedTs, inclusive: true);

                var result = await repo.CheckMessageGapAsync(
                    Channel.ChannelUrl,
                    prevStartTs: cachedOldestTs,
                    prevEndTs: _oldestSyncedTs,
                    prevCacheCount: prevCacheCount,
                    nextStartTs: _latestSyncedTs,
                    nextEndTs: cachedLatestTs,
                    nextCacheCount: nextCacheCount
                );

                if (result.IsHugeGap)
                {
                    Logger.Warning(LogCategory.Message, "Huge gap detected, notifying delegate");
                    DispatchToMainThread(() =>
                    {
                        if (_isDisposed) return;
                        Delegate?.OnHugeGapDetected(this);
                    });
                    return;
                }

                // Fill missing messages
                var totalMessagesToFill = new List<GimBaseMessage>();

                if (result.PrevMessages != null && result.PrevMessages.Count > 0)
                {
                    var prevMessages = result.PrevMessages.Select(MessageBoMapper.ToPublicModel).ToList();
                    totalMessagesToFill.AddRange(prevMessages);

                    long oldestTs = prevMessages.Min(m => m.CreatedAt);
                    _oldestSyncedTs = Math.Min(_oldestSyncedTs, oldestTs);

                    // If server indicates more available, may need additional handling (simplified here, or use fillPrevGap like iOS)
                }

                if (result.NextMessages != null && result.NextMessages.Count > 0)
                {
                    var nextMessages = result.NextMessages.Select(MessageBoMapper.ToPublicModel).ToList();
                    totalMessagesToFill.AddRange(nextMessages);
                    
                    long latestTs = nextMessages.Max(m => m.CreatedAt);
                    _latestSyncedTs = Math.Max(_latestSyncedTs, latestTs);
                }

                if (totalMessagesToFill.Count > 0)
                {
                    var inserted = _cachedMessages.InsertAllIfNotExist(totalMessagesToFill);
                    
                    if (inserted.Count > 0)
                    {
                        Logger.Info(LogCategory.Message, $"Filled gap with {inserted.Count} messages");
                        DispatchToMainThread(() =>
                        {
                            if (_isDisposed) return;
                            var context = new GimMessageContext(GimCollectionEventSource.MessageChangelog, GimMessageSendingStatus.Succeeded);
                            Delegate?.OnMessagesAdded(this, context, Channel, inserted);
                        });
                    }
                }

                Logger.Info(LogCategory.Message, "Huge gap check completed");
            }
            catch (GimException ex)
            {
                Logger.Error(LogCategory.Message, $"Huge gap check failed: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // Testing Helpers
        // ══════════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
        /// <summary>
        /// [Testing] Injects new message, simulating WebSocket receiving MESG.
        /// Uses ChannelUrl from message to create channel for testing channel filtering logic.
        /// </summary>
        internal void InjectMessageForTesting(GimBaseMessage message)
        {
            var msgChannel = new GimGroupChannel { ChannelUrl = message.ChannelUrl };
            HandleMessageReceived(msgChannel, message);
        }

        /// <summary>
        /// [Testing] Injects message update, simulating WebSocket receiving MEDI.
        /// </summary>
        internal void InjectMessageUpdateForTesting(GimBaseMessage message)
        {
            var msgChannel = new GimGroupChannel { ChannelUrl = message.ChannelUrl };
            HandleMessageUpdated(msgChannel, message);
        }

        /// <summary>
        /// [Testing] Injects message deletion, simulating WebSocket receiving delete event.
        /// </summary>
        internal void InjectMessageDeleteForTesting(long messageId)
        {
            HandleMessageDeleted(Channel, messageId);
        }

        /// <summary>
        /// [Testing] Injects pending message event, simulating local message send start.
        /// </summary>
        internal void InjectMessagePendingForTesting(GimBaseMessage message)
        {
            var msgChannel = new GimGroupChannel { ChannelUrl = message.ChannelUrl };
            HandleMessagePending(msgChannel, message);
        }

        /// <summary>
        /// [Testing] Injects sent message event, simulating server ACK.
        /// </summary>
        internal void InjectMessageSentForTesting(GimBaseMessage message)
        {
            var msgChannel = new GimGroupChannel { ChannelUrl = message.ChannelUrl };
            HandleMessageSent(msgChannel, message);
        }

        /// <summary>
        /// [Testing] Injects failed message event, simulating send failure.
        /// </summary>
        internal void InjectMessageFailedForTesting(GimBaseMessage message, GimException error)
        {
            var msgChannel = new GimGroupChannel { ChannelUrl = message.ChannelUrl };
            HandleMessageFailed(msgChannel, message, error);
        }
#endif
    }
}
