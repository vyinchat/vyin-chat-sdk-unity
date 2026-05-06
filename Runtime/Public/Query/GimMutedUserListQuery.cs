using System.Collections.Generic;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Domain.Repositories;
using Gamania.GIMChat.Internal.Platform;
using Gamania.GIMChat.Internal.Platform.Unity;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Query for fetching muted users in a channel with pagination.
    /// </summary>
    public class GimMutedUserListQuery : BaseUserListQuery<GimRestrictedUser>
    {
        /// <summary>
        /// The type of channel being queried.
        /// </summary>
        public GimChannelType ChannelType { get; }

        /// <summary>
        /// The URL of the channel being queried.
        /// </summary>
        public string ChannelUrl { get; }

        internal GimMutedUserListQuery(GimMutedUserListQueryParams parameters)
            : base(parameters?.Limit ?? 20)
        {
            if (parameters == null)
            {
                throw new GimException(GimErrorCode.InvalidParameter, "Parameters cannot be null.");
            }
            if (string.IsNullOrEmpty(parameters.ChannelUrl))
            {
                throw new GimException(GimErrorCode.InvalidParameter, "ChannelUrl is required.");
            }

            ChannelType = parameters.ChannelType;
            ChannelUrl = parameters.ChannelUrl;
        }

        /// <inheritdoc />
        protected override async Task<(List<GimRestrictedUser> Users, string NextToken)> FetchNextAsync()
        {
            var repo = GIMChatMain.Instance.GetChannelRepository();
            var result = await repo.GetMutedUserListAsync(
                channelType: ChannelType,
                channelUrl: ChannelUrl,
                token: Token,
                limit: Limit);

            var users = new List<GimRestrictedUser>();
            if (result?.Users != null)
            {
                foreach (var u in result.Users)
                {
                    var restrictionInfo = new GimRestrictionInfo(
                        u.Description,
                        u.EndAt,
                        GimRestrictionType.Muted);

                    users.Add(new GimRestrictedUser(
                        u.UserId,
                        u.Nickname,
                        u.ProfileUrl,
                        restrictionInfo));
                }
            }

            return (users, result?.NextToken);
        }
    }
}
