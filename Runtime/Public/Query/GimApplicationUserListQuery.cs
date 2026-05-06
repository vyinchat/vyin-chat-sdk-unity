using System.Collections.Generic;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Domain.Repositories;
using Gamania.GIMChat.Internal.Platform;
using Gamania.GIMChat.Internal.Platform.Unity;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Query for fetching application users with pagination.
    /// Supports filtering by user IDs, nickname prefix, and metadata.
    /// </summary>
    public class GimApplicationUserListQuery : BaseUserListQuery<GimUser>
    {
        /// <summary>
        /// Filter results to only include users with these IDs.
        /// </summary>
        public IReadOnlyList<string> UserIdsFilter { get; }

        /// <summary>
        /// Filter results to only include users whose nickname starts with this string.
        /// </summary>
        public string NicknameStartsWithFilter { get; }

        /// <summary>
        /// Filter results by metadata.
        /// </summary>
        public (string Key, List<string> Values)? MetaDataFilter { get; }

        internal GimApplicationUserListQuery(GimApplicationUserListQueryParams parameters)
            : base(parameters?.Limit ?? 20)
        {
            var p = parameters ?? new GimApplicationUserListQueryParams();
            UserIdsFilter = p.UserIdsFilter?.AsReadOnly();
            NicknameStartsWithFilter = p.NicknameStartsWithFilter;
            MetaDataFilter = p.MetaDataFilter;
        }

        /// <inheritdoc />
        protected override async Task<(List<GimUser> Users, string NextToken)> FetchNextAsync()
        {
            var repo = GIMChatMain.Instance.GetUserRepository();
            var userIdsList = UserIdsFilter != null ? new List<string>(UserIdsFilter) : null;
            var result = await repo.GetUserListAsync(
                token: Token,
                limit: Limit,
                nicknameStartsWith: NicknameStartsWithFilter,
                userIds: userIdsList,
                metaDataFilter: MetaDataFilter);

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
