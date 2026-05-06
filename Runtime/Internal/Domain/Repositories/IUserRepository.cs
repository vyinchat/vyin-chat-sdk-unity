using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Gamania.GIMChat.Internal.Domain.Repositories
{
    /// <summary>
    /// Repository interface for user operations.
    /// </summary>
    public interface IUserRepository
    {
        /// <summary>
        /// Updates user information.
        /// </summary>
        /// <param name="userId">User ID to update.</param>
        /// <param name="nickname">New nickname (null to keep unchanged).</param>
        /// <param name="profileUrl">New profile URL (null to keep unchanged).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Updated user info.</returns>
        Task<UserUpdateResult> UpdateUserInfoAsync(
            string userId,
            string nickname,
            string profileUrl,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a paginated list of application users.
        /// </summary>
        /// <param name="token">Pagination token (null for first page).</param>
        /// <param name="limit">Max users per page.</param>
        /// <param name="nicknameStartsWith">Filter by nickname prefix.</param>
        /// <param name="userIds">Filter by user IDs.</param>
        /// <param name="metaDataFilter">Filter by metadata key-values.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>User list result with pagination token.</returns>
        Task<UserListResult> GetUserListAsync(
            string token,
            int limit,
            string nicknameStartsWith,
            List<string> userIds,
            (string Key, List<string> Values)? metaDataFilter,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets a paginated list of users blocked by the specified user.
        /// </summary>
        /// <param name="userId">The user whose blocked list to retrieve.</param>
        /// <param name="token">Pagination token (null for first page).</param>
        /// <param name="limit">Max users per page.</param>
        /// <param name="userIds">Filter by user IDs.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>User list result with pagination token.</returns>
        Task<UserListResult> GetBlockedUserListAsync(
            string userId,
            string token,
            int limit,
            List<string> userIds,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Blocks a user.
        /// </summary>
        /// <param name="currentUserId">The current user's ID.</param>
        /// <param name="targetUserId">The user ID to block.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The blocked user info.</returns>
        Task<UserBO> BlockUserAsync(
            string currentUserId,
            string targetUserId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Unblocks a user.
        /// </summary>
        /// <param name="currentUserId">The current user's ID.</param>
        /// <param name="targetUserId">The user ID to unblock.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task UnblockUserAsync(
            string currentUserId,
            string targetUserId,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Result of user update operation.
    /// </summary>
    public class UserUpdateResult
    {
        public string UserId { get; set; }
        public string Nickname { get; set; }
        public string ProfileUrl { get; set; }
    }

    /// <summary>
    /// Result of user list query.
    /// </summary>
    public class UserListResult
    {
        public List<UserBO> Users { get; set; }
        public string NextToken { get; set; }
    }

    /// <summary>
    /// User business object.
    /// </summary>
    public class UserBO
    {
        public string UserId { get; set; }
        public string Nickname { get; set; }
        public string ProfileUrl { get; set; }
    }
}
