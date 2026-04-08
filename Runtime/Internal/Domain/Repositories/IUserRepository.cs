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
}
