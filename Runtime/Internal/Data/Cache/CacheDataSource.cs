using System.Collections.Generic;
using System.Linq;

namespace Gamania.GIMChat.Internal.Data.Cache
{
    /// <summary>
    /// Shared in-memory cache singleton.
    /// Implements pending/failed message queues, channel cache, and user cache.
    /// </summary>
    internal class CacheDataSource
    {
        public static CacheDataSource Instance { get; } = new CacheDataSource();

        private readonly Dictionary<string, List<GimBaseMessage>> _pending = new();
        private readonly Dictionary<string, List<GimBaseMessage>> _failed = new();
        private readonly Dictionary<string, GimGroupChannel> _channels = new();
        private readonly Dictionary<string, GimUser> _users = new();

        // ── Pending messages ──────────────────────────────────────────────────────

        /// <summary>
        /// Returns all pending messages for the given channel.
        /// </summary>
        public IReadOnlyList<GimBaseMessage> GetPendingMessages(string channelUrl)
        {
            return _pending.TryGetValue(channelUrl, out var list)
                ? list.AsReadOnly()
                : new List<GimBaseMessage>().AsReadOnly();
        }

        /// <summary>
        /// Stores a pending message (MessageId == 0, ReqId set) for its channel.
        /// </summary>
        public void AddPendingMessage(GimBaseMessage message)
        {
            if (!_pending.TryGetValue(message.ChannelUrl, out var list))
            {
                list = new List<GimBaseMessage>();
                _pending[message.ChannelUrl] = list;
            }
            list.Add(message);
        }

        /// <summary>
        /// Removes the pending message with the given reqId from the channel.
        /// No-op if not found.
        /// </summary>
        public void RemovePendingMessage(string reqId, string channelUrl)
        {
            if (!_pending.TryGetValue(channelUrl, out var list)) return;
            list.RemoveAll(m => m.ReqId == reqId);
        }

        // ── Failed messages ───────────────────────────────────────────────────────

        /// <summary>
        /// Returns all failed messages for the given channel.
        /// </summary>
        public IReadOnlyList<GimBaseMessage> GetFailedMessages(string channelUrl)
        {
            return _failed.TryGetValue(channelUrl, out var list)
                ? list.AsReadOnly()
                : new List<GimBaseMessage>().AsReadOnly();
        }

        /// <summary>
        /// Stores a failed message for its channel.
        /// </summary>
        public void AddFailedMessage(GimBaseMessage message)
        {
            if (!_failed.TryGetValue(message.ChannelUrl, out var list))
            {
                list = new List<GimBaseMessage>();
                _failed[message.ChannelUrl] = list;
            }
            list.Add(message);
        }

        /// <summary>
        /// Removes the specified messages from the channel's failed list.
        /// Matches by MessageId.
        /// </summary>
        public void DeleteFailedMessages(IEnumerable<GimBaseMessage> messages, string channelUrl)
        {
            if (!_failed.TryGetValue(channelUrl, out var list)) return;
            var ids = new HashSet<long>(messages.Select(m => m.MessageId));
            list.RemoveAll(m => ids.Contains(m.MessageId));
        }

        /// <summary>
        /// Removes all failed messages for the given channel.
        /// </summary>
        public void DeleteAllFailedMessages(string channelUrl)
        {
            _failed.Remove(channelUrl);
        }

        // ── Channel cache (for GroupChannelCollection) ────────────────────────────

        /// <summary>
        /// Adds or updates a channel in the shared cache.
        /// Called when LoadMore returns channels or when real-time events update a channel.
        /// </summary>
        public void SetChannel(GimGroupChannel channel)
        {
            if (channel == null || string.IsNullOrEmpty(channel.ChannelUrl)) return;
            _channels[channel.ChannelUrl] = channel;
        }

        /// <summary>
        /// Adds or updates multiple channels in the shared cache.
        /// </summary>
        public void SetChannels(IEnumerable<GimGroupChannel> channels)
        {
            if (channels == null) return;
            foreach (var ch in channels)
                SetChannel(ch);
        }

        /// <summary>
        /// Returns channels from cache for the given URIs, in the order of uris.
        /// Missing channels are skipped.
        /// </summary>
        public IReadOnlyList<GimGroupChannel> GetGroupChannelsFromCache(IEnumerable<string> uris)
        {
            if (uris == null) return new List<GimGroupChannel>().AsReadOnly();
            var result = new List<GimGroupChannel>();
            foreach (var url in uris)
            {
                if (string.IsNullOrEmpty(url)) continue;
                if (_channels.TryGetValue(url, out var ch))
                    result.Add(ch);
            }
            return result.AsReadOnly();
        }

        /// <summary>
        /// Removes a channel from cache (e.g., when channel is deleted).
        /// </summary>
        public void RemoveChannel(string channelUrl)
        {
            if (!string.IsNullOrEmpty(channelUrl))
                _channels.Remove(channelUrl);
        }

        // ── User cache ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the cached user with the given userId, or null if not found.
        /// </summary>
        public GimUser GetUserFromCache(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return null;
            return _users.TryGetValue(userId, out var user) ? user : null;
        }

        /// <summary>
        /// Adds or updates a user in the cache.
        /// </summary>
        public void UpsertUser(GimUser user)
        {
            if (user == null || string.IsNullOrEmpty(user.UserId)) return;
            _users[user.UserId] = user;
        }

        /// <summary>
        /// Checks if the new user info differs from the cached version.
        /// Compares Nickname and ProfileUrl fields.
        /// Returns true if there are differences, false if identical or user not in cache.
        /// </summary>
        public bool HasUserInfoChanged(GimUser newUser)
        {
            if (newUser == null || string.IsNullOrEmpty(newUser.UserId)) return false;

            if (!_users.TryGetValue(newUser.UserId, out var cachedUser))
                return false; // Not in cache, no "change" to report

            return cachedUser.Nickname != newUser.Nickname
                || cachedUser.ProfileUrl != newUser.ProfileUrl;
        }

        /// <summary>
        /// Checks if the user info has changed and updates the cache if so.
        /// Returns true if the user info was changed and updated, false otherwise.
        /// </summary>
        public bool CheckAndUpdateUserIfChanged(GimUser newUser)
        {
            if (newUser == null || string.IsNullOrEmpty(newUser.UserId)) return false;

            if (!_users.TryGetValue(newUser.UserId, out var cachedUser))
            {
                // Not in cache, insert it
                _users[newUser.UserId] = newUser;
                return false; // No "change" event, just initial cache
            }

            if (cachedUser.Nickname != newUser.Nickname || cachedUser.ProfileUrl != newUser.ProfileUrl)
            {
                // User info changed, update cache
                _users[newUser.UserId] = newUser;
                return true;
            }

            return false;
        }

        // ── Reset ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Clears all in-memory data. Intended for testing or full session reset.
        /// </summary>
        public void Clear()
        {
            _pending.Clear();
            _failed.Clear();
            _channels.Clear();
            _users.Clear();
        }
    }
}
