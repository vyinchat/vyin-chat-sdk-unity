using System.Collections.Generic;
using Gamania.GIMChat.Internal.Data.Cache;
using Gamania.GIMChat.Internal.Domain.Log;

namespace Gamania.GIMChat.Internal
{
    /// <summary>
    /// Internal event routing hub for SDK Collections and User Events.
    ///
    /// Collections register themselves as internal channel event listeners
    /// without exposing the mechanism to external app code.
    ///
    /// Usage in a concrete Collection:
    /// <code>
    ///   DelegateManager.AddChannelHandler(_handlerId, new GimGroupChannelHandler {
    ///       OnMessageReceived = HandleMessageReceived,
    ///       OnMessageUpdated  = HandleMessageUpdated,
    ///   });
    ///   // On dispose:
    ///   DelegateManager.RemoveChannelHandler(_handlerId);
    /// </code>
    /// </summary>
    internal class GimSdkDelegateManager
    {
        public static GimSdkDelegateManager Instance { get; } = new GimSdkDelegateManager();

        private readonly Dictionary<string, GimUserEventHandler> _userEventHandlers = new();

        // ── Channel events ────────────────────────────────────────────────────────

        /// <summary>
        /// Registers a handler to receive channel events (OnMessageReceived, OnMessageUpdated).
        /// If a handler with the same identifier already exists it is replaced.
        /// </summary>
        public void AddChannelHandler(string identifier, GimGroupChannelHandler handler)
        {
            // Remove first to allow re-registration / replacement
            GimGroupChannel.RemoveGroupChannelHandler(identifier);
            GimGroupChannel.AddGroupChannelHandler(identifier, handler);
        }

        /// <summary>
        /// Returns the channel handler registered with the given identifier, or null.
        /// </summary>
        public GimGroupChannelHandler GetChannelHandler(string identifier)
        {
            return GimGroupChannel.GetGroupChannelHandler(identifier);
        }

        /// <summary>
        /// Unregisters the channel event handler with the given identifier.
        /// </summary>
        public void RemoveChannelHandler(string identifier)
        {
            GimGroupChannel.RemoveGroupChannelHandler(identifier);
        }

        /// <summary>
        /// Unregisters all channel event handlers.
        /// Used during disconnect/logout to clean up all listeners.
        /// </summary>
        public void RemoveAllChannelHandlers()
        {
            GimGroupChannel.RemoveAllGroupChannelHandlers();
        }

        // ── User events ────────────────────────────────────────────────────────────

        /// <summary>
        /// Registers a handler to receive user events (OnUserInfoUpdated).
        /// If a handler with the same identifier already exists it is replaced.
        /// </summary>
        public void AddUserEventHandler(string identifier, GimUserEventHandler handler)
        {
            if (string.IsNullOrEmpty(identifier) || handler == null) return;
            _userEventHandlers[identifier] = handler;
        }

        /// <summary>
        /// Returns the user event handler registered with the given identifier, or null.
        /// </summary>
        public GimUserEventHandler GetUserEventHandler(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;
            return _userEventHandlers.TryGetValue(identifier, out var handler) ? handler : null;
        }

        /// <summary>
        /// Unregisters the user event handler with the given identifier.
        /// Returns the removed handler, or null if not found.
        /// </summary>
        public GimUserEventHandler RemoveUserEventHandler(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return null;
            if (_userEventHandlers.TryGetValue(identifier, out var handler))
            {
                _userEventHandlers.Remove(identifier);
                return handler;
            }
            return null;
        }

        /// <summary>
        /// Unregisters all user event handlers.
        /// Used during disconnect/logout to clean up all listeners.
        /// </summary>
        public void RemoveAllUserEventHandlers()
        {
            _userEventHandlers.Clear();
        }

        /// <summary>
        /// Returns all registered user event handlers.
        /// </summary>
        internal IEnumerable<GimUserEventHandler> GetAllUserEventHandlers()
        {
            return _userEventHandlers.Values;
        }

        /// <summary>
        /// Notifies all registered user event handlers that user info has been updated.
        /// </summary>
        internal void NotifyUserInfoUpdated(IReadOnlyList<GimUser> users)
        {
            if (users == null || users.Count == 0) return;
            foreach (var handler in _userEventHandlers.Values)
            {
                handler.OnUserInfoUpdated?.Invoke(users);
            }
        }

        // ── User info change detection ────────────────────────────────────────────

        /// <summary>
        /// Checks if the sender info has changed compared to the cached version.
        /// If changed, updates the cache and notifies all handlers.
        /// For current user: updates properties silently (no event) to sync from server.
        /// </summary>
        /// <param name="sender">The sender from the incoming message.</param>
        /// <param name="currentUser">The current logged-in user (to update properties if sender is self).</param>
        internal void CheckAndNotifySenderInfoChanged(GimSender sender, GimUser currentUser)
        {
            if (sender == null || string.IsNullOrEmpty(sender.UserId))
                return;

            // Current user: sync silently (no event), only if changed
            if (currentUser != null && sender.UserId == currentUser.UserId)
            {
                if (UpdateUserProfileIfChanged(currentUser, sender.Nickname, sender.ProfileUrl))
                {
                    Logger.Debug(LogCategory.User, $"Current user profile synced: '{sender.Nickname}'");
                }
                return;
            }

            if (UpdateUserCacheIfChanged(sender))
            {
                NotifyUserInfoUpdated(new List<GimUser> { sender });
            }
        }

        /// <summary>
        /// Checks if the user info differs from the cached version and updates if so.
        /// </summary>
        /// <returns>True if the user info was changed and cache was updated.</returns>
        private bool UpdateUserCacheIfChanged(GimUser user)
        {
            var cache = CacheDataSource.Instance;
            var cachedUser = cache.GetUserFromCache(user.UserId);

            if (cachedUser == null)
            {
                // First time seeing this user, add to cache (no "change" event)
                cache.UpsertUser(user);
                Logger.Debug(LogCategory.User, $"User {user.UserId} added to cache");
                return false;
            }

            // Compare nickname and profileUrl
            if (cachedUser.Nickname != user.Nickname || cachedUser.ProfileUrl != user.ProfileUrl)
            {
                cache.UpsertUser(user);
                Logger.Debug(LogCategory.User,
                    $"User {user.UserId} info changed: '{cachedUser.Nickname}' → '{user.Nickname}'");
                return true;
            }

            return false;
        }

        private static bool UpdateUserProfileIfChanged(GimUser user, string nickname, string profileUrl)
        {
            bool changed = false;
            if (user.Nickname != nickname) { user.Nickname = nickname; changed = true; }
            if (user.ProfileUrl != profileUrl) { user.ProfileUrl = profileUrl; changed = true; }
            return changed;
        }
    }
}
