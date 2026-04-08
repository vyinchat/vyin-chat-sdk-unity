using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Data.Cache;
using Gamania.GIMChat.Internal.Domain.Log;
using Gamania.GIMChat.Internal.Domain.Mappers;
using Gamania.GIMChat.Internal.Domain.Repositories;
using Gamania.GIMChat.Internal.Platform;
using Gamania.GIMChat.Internal.Platform.Unity;
using Logger = Gamania.GIMChat.Internal.Domain.Log.Logger;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Group Channel Collection - Manages a paginated, real-time-updated list of group channels.
    ///
    /// Lifecycle: construct → LoadMore → Dispose.
    ///
    /// Features:
    /// 1. READ: Load channel list (LoadMore)
    /// 2. UPDATE: Real-time channel updates (HandleChannelUpdated)
    /// 3. DELETE: Remove deleted channels (HandleChannelDeleted)
    ///
    /// Implementation is TDD-driven; this stub defines the public API contract.
    /// </summary>
    public sealed class GimGroupChannelCollection : GimBaseCollection, IDisposable
    {
        // ═══════════════════════════════════════════════════════════════════════════
        // Private Fields
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>Whether disposed</summary>
        private bool _isDisposed;

        /// <summary>
        /// Tracks which channel URLs are in this collection (identified by URL)
        /// </summary>
        private readonly HashSet<string> _channelUri = new();

        /// <summary>
        /// Query object that owns token, limit, and load logic
        /// </summary>
        private readonly GimGroupChannelListQuery _query;

        /// <summary>
        /// Unique identifier for registering/unregistering event handlers
        /// </summary>
        private readonly string _channelIdentifier;

        /// <summary>
        /// Semaphore for serializing changelog operations (ensures only one request at a time)
        /// </summary>
        private readonly SemaphoreSlim _changelogLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Last successful changelog sync timestamp in milliseconds
        /// </summary>
        private long? _lastSyncTime;

        // ═══════════════════════════════════════════════════════════════════════════
        // Public Properties
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Currently loaded channels, sorted by last message time desc.
        /// Re-fetched and sorted from cache on each access.
        /// </summary>
        public IReadOnlyList<GimGroupChannel> ChannelList
        {
            get
            {
                // Get all channels tracked by this Collection from cache
                var channels = DataSource.GetGroupChannelsFromCache(_channelUri);
                // Sort and return (latest message first)
                return SortChannels(channels).ToList().AsReadOnly();
            }
        }

        /// <summary>
        /// Whether more channels can be loaded via LoadMore. Delegates to Query.HasNext.
        /// </summary>
        public bool HasNext => _query.HasNext;

        /// <summary>
        /// Whether a pagination request is currently in flight. Delegates to Query.IsLoading.
        /// </summary>
        public bool IsLoading => _query.IsLoading;

        /// <summary>
        /// Group channel list query (contains limit, token, and other pagination params)
        /// </summary>
        public GimGroupChannelListQuery Query => _query;

        /// <summary>
        /// True after Dispose() has been called.
        /// </summary>
        public bool IsDisposed => _isDisposed;

        /// <summary>
        /// Repository for backend API access (internal use)
        /// </summary>
        private IChannelRepository Repository => GIMChatMain.Instance.GetChannelRepository();

        /// <summary>
        /// Delegate to receive collection events (channels added/updated/deleted).
        /// </summary>
        public IGimGroupChannelCollectionDelegate Delegate { get; set; }

        // ═══════════════════════════════════════════════════════════════════════════
        // Constructor
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates a GroupChannelCollection with the given query.
        /// Immediately begins observing channel events.
        /// </summary>
        /// <param name="query">Query that owns token, limit, and load logic.</param>
        public GimGroupChannelCollection(GimGroupChannelListQuery query)
        {
            _query = query ?? throw new ArgumentNullException(nameof(query));
            // Generate unique identifier: gcc_{GUID}_{timestamp}
            _channelIdentifier = $"gcc_{Guid.NewGuid():N}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            // Immediately start observing channel events
            StartObserve();
        }

        /// <summary>
        /// Registers channel and connection event handlers
        /// </summary>
        private void StartObserve()
        {
            // Register channel event handler
            DelegateManager.AddChannelHandler(_channelIdentifier, new GimGroupChannelHandler
            {
                OnMessageReceived = HandleMessageReceived,  // When message received
                OnMessageUpdated = HandleMessageUpdated,    // When message updated
                OnChannelChanged = HandleChannelChanged,    // When channel properties changed
                OnChannelDeleted = HandleChannelDeleted     // When channel deleted
            });

            // Register connection event handler (for auto changelog sync on reconnect)
            GIMChatMain.Instance.AddConnectionHandler(_channelIdentifier, new GimConnectionHandler
            {
                OnReconnectSucceeded = HandleReconnectSucceeded  // Auto-trigger Changelog on reconnect
            });
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Event Handlers
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Handles channel property changes (e.g., name, cover image)
        /// </summary>
        private void HandleChannelChanged(GimGroupChannel channel)
        {
            if (_isDisposed || channel == null) return;
            HandleChannelUpdated(channel, GimCollectionEventSource.EventChannelChanged);
        }

        /// <summary>
        /// Handles channel deletion
        /// </summary>
        private void HandleChannelDeleted(string channelUrl)
        {
            // Guard: disposed or empty URL → return
            if (_isDisposed || string.IsNullOrEmpty(channelUrl)) return;
            // Remove from tracking set, return if not in collection
            if (!_channelUri.Remove(channelUrl)) return;

            // Remove from cache
            DataSource.RemoveChannel(channelUrl);
            // Create event context
            var context = new GimChannelContext(GimCollectionEventSource.EventChannelDeleted);
            var urls = new[] { channelUrl };
            // Trigger delegate callback on main thread
            DispatchToMainThread(() =>
            {
                if (_isDisposed) return;  // Re-check to prevent dispose during dispatch
                Delegate?.OnChannelsDeleted(this, context, urls);
            });
        }

        /// <summary>
        /// Handles message received event (triggers channel update due to lastMessage change)
        /// </summary>
        private void HandleMessageReceived(GimGroupChannel channel, GimBaseMessage message)
        {
            if (_isDisposed || channel == null) return;
            HandleChannelUpdated(channel, GimCollectionEventSource.EventMessageReceived);
        }

        /// <summary>
        /// Handles message updated event (triggers channel update)
        /// </summary>
        private void HandleMessageUpdated(GimGroupChannel channel, GimBaseMessage message)
        {
            if (_isDisposed || channel == null) return;
            HandleChannelUpdated(channel, GimCollectionEventSource.EventMessageUpdated);
        }

        /// <summary>
        /// Handles reconnection success - automatically triggers changelog sync
        /// When user reconnects after being offline, automatically syncs missed channel changes
        /// </summary>
        private void HandleReconnectSucceeded()
        {
            if (_isDisposed) return;

            Logger.Info(LogCategory.Channel, "Reconnected, requesting changelogs");

            // Execute changelog in background (fire and forget, don't block UI)
            _ = RequestChangeLogsAsync();
        }

        /// <summary>
        /// Core logic for handling channel updates
        ///
        /// Flow:
        /// 1. Check if channel is already in Collection
        /// 2. Update channel data in cache
        /// 3. Add to tracking set
        /// 4. Trigger OnChannelsAdded or OnChannelsUpdated based on whether it was in collection
        /// </summary>
        private void HandleChannelUpdated(GimGroupChannel channel, GimCollectionEventSource source)
        {
            if (string.IsNullOrEmpty(channel?.ChannelUrl)) return;

            // Check if channel is already in Collection
            var wasInCollection = _channelUri.Contains(channel.ChannelUrl);

            // Update channel data in cache (UPSERT: update if exists, add if not)
            DataSource.SetChannel(channel);
            // Add to tracking set (no effect if already exists)
            _channelUri.Add(channel.ChannelUrl);

            // Create event context
            var context = new GimChannelContext(source);
            var channels = new[] { channel };

            // Trigger delegate callback on main thread
            DispatchToMainThread(() =>
            {
                if (_isDisposed) return;  // Re-check to prevent dispose during dispatch
                if (wasInCollection)
                    Delegate?.OnChannelsUpdated(this, context, channels);  // Exists → Update
                else
                    Delegate?.OnChannelsAdded(this, context, channels);    // New → Add
            });
        }

        /// <summary>
        /// Dispatches action to Unity main thread (UI operations must run on main thread)
        /// </summary>
        private void DispatchToMainThread(Action action)
        {
            MainThreadDispatcher.Enqueue(action);
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Lifecycle Management
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Releases resources, unregisters channel event handlers.
        ///
        /// After disposal:
        /// - All event handlers will be ignored (early return)
        /// - Cannot call LoadMore (will throw GimException)
        /// - Need to create a new Collection instance
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed) return;  // Idempotent: repeated calls have no effect
            _isDisposed = true;
            // Unregister event listeners (Channel + Connection)
            DelegateManager.RemoveChannelHandler(_channelIdentifier);
            GIMChatMain.Instance.RemoveConnectionHandler(_channelIdentifier);
            // Clear tracking set
            _channelUri.Clear();
            // Dispose changelog lock
            _changelogLock?.Dispose();
        }

        /// <summary>
        /// Guards against operations on disposed collection
        /// </summary>
        private void GuardDisposed()
        {
            if (_isDisposed)
                throw new GimException(
                    GimErrorCode.CollectionDisposed,
                    "GimGroupChannelCollection has been disposed. Create a new instance.");
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Load More Channels - Callback API
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Loads the next page of channels (Callback version)
        /// </summary>
        /// <param name="completionHandler">Completion callback (channels, error)</param>
        public void LoadMore(GimGroupChannelListHandler completionHandler)
        {
            GuardDisposed();  // Check if disposed
            if (completionHandler == null)
            {
                Logger.Warning(LogCategory.Channel, "LoadMore: completionHandler is null");
                return;
            }
            // Internal async implementation bridged to callback via Helper
            _ = AsyncCallbackHelper.ExecuteAsync(
                () => LoadMoreCoreAsync(),
                (channels, error) => completionHandler(channels, error),
                "GimGroupChannelCollection.LoadMore",
                "LoadMore"
            );
        }

        // ═══════════════════════════════════════════════════════════════════════════
        // Load More Channels - Async API
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Loads the next page of channels asynchronously
        /// </summary>
        /// <returns>Loaded and sorted channels</returns>
        public async Task<IReadOnlyList<GimGroupChannel>> LoadMoreAsync()
        {
            GuardDisposed();  // Check if disposed
            return await LoadMoreCoreAsync();
        }

        /// <summary>
        /// Core logic for loading more channels
        ///
        /// Flow:
        /// 1. Check HasNext (no more → return empty list)
        /// 2. Check IsLoading (already loading → return empty list)
        /// 3. Call Query.LoadNextAsync() to get next page
        /// 4. Add new channels to tracking set (_channelUri)
        /// 5. Update cache (DataSource)
        /// 6. Sort and return
        /// 7. [First LoadMore] Auto-trigger Changelog sync
        /// </summary>
        private async Task<IReadOnlyList<GimGroupChannel>> LoadMoreCoreAsync()
        {
            // Guard: no more channels to load
            if (!_query.HasNext)
            {
                Logger.Info(LogCategory.Channel, "No more channels to load.");
                return new List<GimGroupChannel>().AsReadOnly();
            }
            // Guard: already loading
            if (_query.IsLoading)
            {
                Logger.Info(LogCategory.Channel, "LoadMore already in progress.");
                return new List<GimGroupChannel>().AsReadOnly();
            }

            // Call Query to load next page (Query manages token and limit internally)
            var channels = await _query.LoadNextAsync();

            // Add newly loaded channels to tracking set
            foreach (var ch in channels)
            {
                if (!string.IsNullOrEmpty(ch?.ChannelUrl))
                    _channelUri.Add(ch.ChannelUrl);
            }
            // Update cache (batch write)
            DataSource.SetChannels(channels);

            // Sort and log
            var sorted = SortChannels(channels).ToList();
            Logger.Info(LogCategory.Channel, $"Loaded {sorted.Count} channels, HasNext={_query.HasNext}");

            // [Auto-trigger Changelog] Sync missed changes after first LoadMore
            if (_lastSyncTime == null)
            {
                Logger.Info(LogCategory.Channel, "First LoadMore completed, auto-requesting changelogs");
                // Fire and forget (don't await to avoid blocking LoadMore return)
                // Pass sorted channels to ensure sync time can be calculated from them
                _ = RequestChangeLogsAsync(sorted);
            }

            return sorted.AsReadOnly();
        }

        /// <summary>
        /// Sort channels by last message created at or created at descending (latest first).
        /// If no lastMessage, use channel.CreatedAt. Add other sort criteria here if needed.
        /// </summary>
        private static IEnumerable<GimGroupChannel> SortChannels(IEnumerable<GimGroupChannel> channels)
            => channels.OrderByDescending(c => c.LastMessage?.CreatedAt ?? c.CreatedAt);

        // ═══════════════════════════════════════════════════════════════════════════
        // Changelog Synchronization
        // ═══════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Requests changelog to sync channel changes during offline period
        ///
        /// Flow:
        /// 1. Calculate syncTime (4 priority levels: PlayerPrefs > first channel > last connection > current time)
        /// 2. Call Repository.GetChangeLogsAsync to get changelog
        /// 3. Process updated channels (trigger OnChannelsUpdated or OnChannelsAdded)
        /// 4. Process deleted channels (trigger OnChannelsDeleted)
        /// 5. Save new syncTime to PlayerPrefs
        /// </summary>
        /// <param name="loadedChannels">Optional channels from LoadMore for sync time calculation fallback</param>
        /// <returns>Whether sync succeeded</returns>
        internal async Task<bool> RequestChangeLogsAsync(IReadOnlyList<GimGroupChannel> loadedChannels = null)
        {
            GuardDisposed();

            // Serialize: only one changelog operation at a time
            if (!await _changelogLock.WaitAsync(0))
            {
                Logger.Info(LogCategory.Channel, "Changelog already in progress");
                return false;
            }

            try
            {
                var syncTime = CalculateSyncTime(loadedChannels);
                var currentUser = GIMChatMain.Instance.GetCurrentUser();
                if (currentUser == null || string.IsNullOrEmpty(currentUser.UserId))
                {
                    Logger.Warning(LogCategory.Channel, "Cannot request changelogs: no current user");
                    return false;
                }

                Logger.Info(LogCategory.Channel, $"Requesting changelogs from timestamp {syncTime}");

                // Call Repository API to get changelog
                var result = await Repository.GetChangeLogsAsync(currentUser.UserId, syncTime);

                // Process updated channels (triggers OnChannelsUpdated or OnChannelsAdded)
                foreach (var channelBo in result.UpdatedChannels)
                {
                    var vcChannel = ChannelBoMapper.ToPublicModel(channelBo);
                    HandleChannelUpdated(vcChannel, GimCollectionEventSource.ChannelChangelog);
                }

                // Process deleted channels (triggers OnChannelsDeleted)
                foreach (var url in result.DeletedChannelUrls)
                {
                    HandleChannelDeleted(url);
                }

                // Update and persist syncTime
                _lastSyncTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                SyncTimeStorage.SaveSyncTime(currentUser.UserId, _lastSyncTime.Value);

                Logger.Info(LogCategory.Channel,
                    $"Changelog synced: {result.UpdatedChannels.Count} updated, {result.DeletedChannelUrls.Count} deleted");

                return true;
            }
            catch (GimException ex)
            {
                Logger.Error(LogCategory.Channel, $"Changelog failed: {ex.Message}");
                return false;
            }
            finally
            {
                _changelogLock.Release();
            }
        }

        /// <summary>
        /// Calculates syncTime with 4 priority levels
        ///
        /// Priority:
        /// 1. Saved syncTime from PlayerPrefs (highest)
        /// 2. First channel's time (from loadedChannels, or ChannelList fallback)
        /// 3. Last connected time (requires GetLastConnectedAt implementation)
        /// 4. Current time (fallback)
        /// </summary>
        /// <param name="loadedChannels">Optional channels passed from LoadMore for reliable access</param>
        private long CalculateSyncTime(IReadOnlyList<GimGroupChannel> loadedChannels = null)
        {
            // Priority 1: Saved syncTime from PlayerPrefs (requires currentUser)
            var currentUser = GIMChatMain.Instance.GetCurrentUser();
            if (currentUser != null)
            {
                var savedTime = SyncTimeStorage.LoadSyncTime(currentUser.UserId);
                if (savedTime.HasValue)
                {
                    Logger.Info(LogCategory.Channel, $"Using saved syncTime: {savedTime.Value}");
                    return savedTime.Value;
                }
            }

            // Priority 2: First channel's time (loadedChannels first for reliability, then ChannelList fallback)
            var firstChannel = loadedChannels?.FirstOrDefault() ?? ChannelList.FirstOrDefault();
            if (firstChannel != null)
            {
                var time = firstChannel.LastMessage?.CreatedAt ?? firstChannel.CreatedAt;
                Logger.Info(LogCategory.Channel, $"Using first channel time: {time}");
                return time;
            }

            // Priority 3: Last connected time (not implemented, requires GIMChatMain API extension)
            // var lastConnected = GIMChat.GetLastConnectedAt();
            // if (lastConnected > 0) return lastConnected;

            // Priority 4: Current time (fallback)
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Logger.Info(LogCategory.Channel, $"Using current time (fallback): {currentTime}");
            return currentTime;
        }
    }
}
