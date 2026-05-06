using System.Collections.Generic;
using System.Linq;
using Gamania.GIMChat.Internal.Domain.Models;

namespace Gamania.GIMChat.Internal.Domain.Mappers
{
    /// <summary>
    /// Mapper for converting between OpenChannelBO and GimOpenChannel (Public API Model)
    /// Domain Layer responsibility: Business Object ↔ Public Model conversion
    /// </summary>
    internal static class OpenGroupChannelBoMapper
    {
        internal static GimOpenChannel ToPublicModel(OpenChannelBO bo)
        {
            if (bo == null) return null;

            return new GimOpenChannel
            {
                ChannelUrl = bo.ChannelUrl,
                Name = bo.Name,
                CoverUrl = bo.CoverUrl,
                CustomType = bo.CustomType,
                Data = bo.Data,
                CreatedAt = bo.CreatedAt,
                IsFrozen = bo.IsFrozen,
                ParticipantCount = bo.ParticipantCount,
                Operators = bo.Operators?.Select(ToGimUser).ToList() ?? new List<GimUser>()
            };
        }

        private static GimUser ToGimUser(UserBO bo)
        {
            if (bo == null) return null;

            return new GimUser
            {
                UserId = bo.UserId,
                Nickname = bo.Nickname,
                ProfileUrl = bo.ProfileUrl
            };
        }
    }
}
