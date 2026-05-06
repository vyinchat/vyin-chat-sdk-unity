using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Platform;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Base class for paginated user list queries.
    /// Provides common pagination logic and loading state protection.
    /// </summary>
    /// <typeparam name="TUser">Type of user returned by this query.</typeparam>
    public abstract class BaseUserListQuery<TUser> where TUser : GimUser
    {
        private readonly object _lock = new object();
        private bool _isLoading;

        /// <summary>
        /// Pagination token for the next page.
        /// </summary>
        protected string Token { get; set; }

        /// <summary>
        /// Maximum number of users to fetch per page.
        /// </summary>
        public int Limit { get; }

        /// <summary>
        /// Whether there are more users to fetch.
        /// </summary>
        public bool HasNext { get; protected set; } = true;

        /// <summary>
        /// Whether a fetch operation is currently in progress.
        /// Thread-safe property.
        /// </summary>
        public bool IsLoading
        {
            get { lock (_lock) return _isLoading; }
            private set { lock (_lock) _isLoading = value; }
        }

        /// <summary>
        /// Creates a new query with the specified limit.
        /// </summary>
        /// <param name="limit">Maximum users per page.</param>
        protected BaseUserListQuery(int limit)
        {
            Limit = limit > 0 ? limit : 20;
        }

        /// <summary>
        /// Fetches the next page of users.
        /// </summary>
        /// <returns>List of users and the next page token.</returns>
        protected abstract Task<(List<TUser> Users, string NextToken)> FetchNextAsync();

        /// <summary>
        /// Fetches the next page of users.
        /// </summary>
        /// <returns>Read-only list of users.</returns>
        /// <exception cref="GimException">
        /// Thrown with <see cref="GimErrorCode.QueryInProgress"/> if a fetch is already in progress.
        /// </exception>
        public async Task<IReadOnlyList<TUser>> NextAsync()
        {
            if (!HasNext)
            {
                return Array.Empty<TUser>();
            }

            if (IsLoading)
            {
                throw new GimException(GimErrorCode.QueryInProgress, "Query is already in progress.");
            }

            IsLoading = true;
            try
            {
                var (users, nextToken) = await FetchNextAsync();
                Token = nextToken;
                HasNext = !string.IsNullOrEmpty(nextToken);
                return users?.AsReadOnly() ?? (IReadOnlyList<TUser>)Array.Empty<TUser>();
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Fetches the next page of users with callback.
        /// Callback is always invoked on the main thread.
        /// </summary>
        /// <param name="handler">Callback with users list and error (null on success).</param>
        public void Next(Action<IReadOnlyList<TUser>, GimException> handler)
        {
            if (!HasNext)
            {
                MainThreadDispatcher.Enqueue(() => handler?.Invoke(Array.Empty<TUser>(), null));
                return;
            }

            if (IsLoading)
            {
                MainThreadDispatcher.Enqueue(() =>
                    handler?.Invoke(null, new GimException(GimErrorCode.QueryInProgress, "Query is already in progress.")));
                return;
            }

            IsLoading = true;
            FetchNextAsync().ContinueWith(task =>
            {
                IsLoading = false;

                if (task.IsFaulted)
                {
                    var ex = task.Exception?.InnerException as GimException
                        ?? new GimException(GimErrorCode.UnknownError, task.Exception?.InnerException?.Message ?? "Unknown error");
                    MainThreadDispatcher.Enqueue(() => handler?.Invoke(null, ex));
                }
                else
                {
                    var (users, nextToken) = task.Result;
                    Token = nextToken;
                    HasNext = !string.IsNullOrEmpty(nextToken);
                    var result = users?.AsReadOnly() ?? (IReadOnlyList<TUser>)Array.Empty<TUser>();
                    MainThreadDispatcher.Enqueue(() => handler?.Invoke(result, null));
                }
            });
        }
    }
}
