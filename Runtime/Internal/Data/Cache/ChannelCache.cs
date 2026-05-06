using System;
using System.Collections.Generic;
using Gamania.GIMChat.Internal.Domain.Models;

namespace Gamania.GIMChat.Internal.Data.Cache
{
    /// <summary>
    /// Cache for channel data to reduce redundant HTTP requests
    /// Uses LRU (Least Recently Used) eviction strategy
    /// </summary>
    public class ChannelCache
    {
        private readonly Dictionary<string, CachedChannel> _cache;
        private readonly LinkedList<string> _lruList;
        private readonly int _maxCacheSize;
        private readonly TimeSpan _defaultTtl;

        public ChannelCache(int maxCacheSize = 200, TimeSpan? defaultTtl = null)
        {
            if (maxCacheSize <= 0)
                throw new ArgumentException("Cache size must be greater than 0", nameof(maxCacheSize));

            _maxCacheSize = maxCacheSize;
            _defaultTtl = defaultTtl ?? TimeSpan.FromMinutes(5);
            _cache = new Dictionary<string, CachedChannel>(maxCacheSize);
            _lruList = new LinkedList<string>();
        }

        /// <summary>
        /// Try to get channel from cache
        /// </summary>
        /// <param name="channelUrl">Channel URL as key</param>
        /// <param name="channel">Cached channel if found and not expired</param>
        /// <returns>True if cache hit and not expired, false otherwise</returns>
        public bool TryGet(string channelUrl, out GroupChannelBO channel)
        {
            if (string.IsNullOrWhiteSpace(channelUrl))
            {
                channel = null;
                return false;
            }

            if (_cache.TryGetValue(channelUrl, out var cached))
            {
                if (!cached.IsExpired)
                {
                    // Cache hit - move to end of LRU list (most recently used)
                    UpdateLruPosition(channelUrl);
                    channel = cached.Channel;
                    return true;
                }

                // Expired - remove from cache
                Remove(channelUrl);
            }

            channel = null;
            return false;
        }

        /// <summary>
        /// Add or update channel in cache
        /// </summary>
        /// <param name="channelUrl">Channel URL as key</param>
        /// <param name="channel">Channel data to cache</param>
        /// <param name="customTtl">Custom TTL (optional, uses default if null)</param>
        public void Set(string channelUrl, GroupChannelBO channel, TimeSpan? customTtl = null)
        {
            if (string.IsNullOrWhiteSpace(channelUrl))
                throw new ArgumentNullException(nameof(channelUrl));

            if (channel == null)
                throw new ArgumentNullException(nameof(channel));

            // If already exists, update it
            if (_cache.ContainsKey(channelUrl))
            {
                Remove(channelUrl);
            }

            // Evict oldest entry if cache is full
            if (_cache.Count >= _maxCacheSize)
            {
                EvictOldest();
            }

            // Add to cache
            var cachedChannel = new CachedChannel
            {
                Channel = channel,
                CachedAt = DateTime.UtcNow,
                Ttl = customTtl ?? _defaultTtl
            };

            _cache[channelUrl] = cachedChannel;
            _lruList.AddLast(channelUrl);
        }

        /// <summary>
        /// Remove channel from cache
        /// </summary>
        public void Remove(string channelUrl)
        {
            if (string.IsNullOrWhiteSpace(channelUrl))
                return;

            if (_cache.Remove(channelUrl))
            {
                _lruList.Remove(channelUrl);
            }
        }

        /// <summary>
        /// Clear all cached channels
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
            _lruList.Clear();
        }

        /// <summary>
        /// Get current cache size
        /// </summary>
        public int Count => _cache.Count;

        /// <summary>
        /// Invalidate (remove) all expired entries
        /// </summary>
        public int InvalidateExpired()
        {
            var expiredKeys = new List<string>();

            foreach (var kvp in _cache)
            {
                if (kvp.Value.IsExpired)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }

            foreach (var key in expiredKeys)
            {
                Remove(key);
            }

            return expiredKeys.Count;
        }

        private void EvictOldest()
        {
            if (_lruList.Count == 0)
                return;

            // Remove least recently used (first in list)
            var oldestKey = _lruList.First.Value;
            Remove(oldestKey);
        }

        private void UpdateLruPosition(string channelUrl)
        {
            // Move to end (most recently used)
            _lruList.Remove(channelUrl);
            _lruList.AddLast(channelUrl);
        }

        /// <summary>
        /// Cached channel data with metadata
        /// </summary>
        private class CachedChannel
        {
            public GroupChannelBO Channel { get; set; }
            public DateTime CachedAt { get; set; }
            public TimeSpan Ttl { get; set; }

            public bool IsExpired => DateTime.UtcNow - CachedAt > Ttl;
        }
    }
}
