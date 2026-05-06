using System.Collections.Generic;
using System.Linq;
using Gamania.GIMChat.Internal.Data.DTOs;
using Gamania.GIMChat.Internal.Domain.Models;

namespace Gamania.GIMChat.Internal.Data.Mappers
{
    /// <summary>
    /// Maps OpenChannel DTOs to Business Objects
    /// </summary>
    internal static class OpenChannelDtoMapper
    {
        internal static OpenChannelBO ToBusinessObject(OpenChannelDTO dto)
        {
            if (dto == null) return null;

            return new OpenChannelBO
            {
                ChannelUrl = dto.channel_url,
                Name = dto.name,
                CoverUrl = dto.cover_url,
                CustomType = dto.custom_type,
                Data = dto.data,
                CreatedAt = dto.created_at,
                IsFrozen = dto.is_frozen,
                ParticipantCount = dto.participant_count,
                Operators = dto.operators?.Select(ToUserBO).ToList() ?? new List<UserBO>()
            };
        }

        internal static UserBO ToUserBO(UserDTO dto)
        {
            if (dto == null) return null;

            return new UserBO
            {
                UserId = dto.user_id,
                Nickname = dto.nickname,
                ProfileUrl = dto.profile_url
            };
        }

    }
}
