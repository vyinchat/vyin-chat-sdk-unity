using System.Collections.Generic;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Domain.Repositories;
using Gamania.GIMChat.Internal.Platform;
using Gamania.GIMChat.Internal.Platform.Unity;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Query for fetching users blocked by the current user with pagination.
    /// </summary>
    public class GimBlockedUserListQuery : BaseUserListQuery<GimUser>
    {
        /// <summary>
        /// Filter results to only include users with these IDs.
        /// </summary>
        public IReadOnlyList<string> UserIdsFilter { get; }

        internal GimBlockedUserListQuery(GimBlockedUserListQueryParams parameters)
            : base(parameters?.Limit ?? 20)
        {
            var p = parameters ?? new GimBlockedUserListQueryParams();
            UserIdsFilter = p.UserIdsFilter?.AsReadOnly();
        }

        /// <inheritdoc />
        protected override async Task<(List<GimUser> Users, string NextToken)> FetchNextAsync()
        {
            var currentUserId = GIMChat.CurrentUser?.UserId;
            if (string.IsNullOrEmpty(currentUserId))
            {
                throw new GimException(GimErrorCode.ConnectionRequired,
                    "User must be connected to query blocked users.");
            }

            var repo = GIMChatMain.Instance.GetUserRepository();
            var userIdsList = UserIdsFilter != null ? new List<string>(UserIdsFilter) : null;
            var result = await repo.GetBlockedUserListAsync(
                userId: currentUserId,
                token: Token,
                limit: Limit,
                userIds: userIdsList);

            var users = new List<GimUser>();
            if (result?.Users != null)
            {
                foreach (var u in result.Users)
                {
                    users.Add(new GimUser
                    {
                        UserId = u.UserId,
                        Nickname = u.Nickname,
                        ProfileUrl = u.ProfileUrl
                    });
                }
            }

            return (users, result?.NextToken);
        }
    }
}
