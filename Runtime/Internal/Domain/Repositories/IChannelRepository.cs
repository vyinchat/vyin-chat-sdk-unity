using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Gamania.GIMChat.Internal.Domain.Models;

namespace Gamania.GIMChat.Internal.Domain.Repositories
{
    internal interface IChannelRepository
    {
        // ── Shared ────────────────────────────────────────────────────────────────

        Task<BaseChannelBO> GetChannelAsync(
            GimChannelType channelType,
            string channelUrl,
            CancellationToken cancellationToken = default);

        Task DeleteChannelAsync(
            GimChannelType channelType,
            string channelUrl,
            CancellationToken cancellationToken = default);

        Task<RestrictedUserListResult> GetBannedUserListAsync(
            GimChannelType channelType,
            string channelUrl,
            string token,
            int limit,
            CancellationToken cancellationToken = default);

        Task<RestrictedUserListResult> GetMutedUserListAsync(
            GimChannelType channelType,
            string channelUrl,
            string token,
            int limit,
            CancellationToken cancellationToken = default);

        Task BanUserAsync(
            GimChannelType channelType,
            string channelUrl,
            string userId,
            int seconds,
            string description,
            CancellationToken cancellationToken = default);

        Task UnbanUserAsync(
            GimChannelType channelType,
            string channelUrl,
            string userId,
            CancellationToken cancellationToken = default);

        Task MuteUserAsync(
            GimChannelType channelType,
            string channelUrl,
            string userId,
            int seconds,
            string description,
            CancellationToken cancellationToken = default);

        Task UnmuteUserAsync(
            GimChannelType channelType,
            string channelUrl,
            string userId,
            CancellationToken cancellationToken = default);

        // ── Group Channel ─────────────────────────────────────────────────────────

        Task<GroupChannelBO> CreateGroupChannelAsync(
            GimGroupChannelCreateParams createParams,
            CancellationToken cancellationToken = default);

        Task<GroupChannelBO> UpdateGroupChannelAsync(
            string channelUrl,
            GimGroupChannelUpdateParams updateParams,
            CancellationToken cancellationToken = default);

        Task<GroupChannelBO> InviteUsersAsync(
            string channelUrl,
            string[] userIds,
            CancellationToken cancellationToken = default);

        Task<ChannelListResult> ListGroupChannelsAsync(
            string userId,
            int limit = 20,
            string token = null,
            IList<string> customTypesFilter = null,
            string customTypeStartsWithFilter = null,
            bool includeEmpty = false,
            CancellationToken cancellationToken = default);

        Task<ChannelChangeLogResult> GetChangeLogsAsync(
            string userId,
            long syncTimestamp,
            string token = null,
            CancellationToken cancellationToken = default);

        // ── Open Channel ──────────────────────────────────────────────────────────

        Task<OpenChannelBO> CreateOpenChannelAsync(
            GimOpenChannelCreateParams createParams,
            CancellationToken cancellationToken = default);

        Task<OpenChannelBO> UpdateOpenChannelAsync(
            string channelUrl,
            GimOpenChannelUpdateParams updateParams,
            CancellationToken cancellationToken = default);

        Task<OpenChannelListResult> ListOpenChannelsAsync(
            int limit = 20,
            string token = null,
            string nameKeyword = null,
            string urlKeyword = null,
            string customType = null,
            bool includeFrozen = true,
            CancellationToken cancellationToken = default);

        Task<int> EnterChannelAsync(
            string channelUrl,
            CancellationToken cancellationToken = default);

        Task<int> ExitChannelAsync(
            string channelUrl,
            CancellationToken cancellationToken = default);

        Task<ParticipantListResult> GetParticipantListAsync(
            string channelUrl,
            string token = null,
            int limit = 20,
            CancellationToken cancellationToken = default);
    }
}
