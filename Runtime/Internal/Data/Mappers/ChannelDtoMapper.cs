using System.Collections.Generic;
using System.Linq;
using Gamania.GIMChat.Internal.Data.DTOs;
using Gamania.GIMChat.Internal.Domain.Models;

namespace Gamania.GIMChat.Internal.Data.Mappers
{
    /// <summary>
    /// Mapper for converting between ChannelDTO and GroupChannelBO
    /// Data Layer responsibility: DTO ↔ Business Object conversion
    /// </summary>
    public static class ChannelDtoMapper
    {
        /// <summary>
        /// Convert ChannelDTO to GroupChannelBO (Business Object)
        /// </summary>
        public static GroupChannelBO ToBusinessObject(ChannelDTO dto)
        {
            if (dto == null)
            {
                return null;
            }

            return new GroupChannelBO
            {
                ChannelUrl = dto.channel_url,
                Name = dto.name,
                CoverUrl = dto.cover_url,
                CustomType = dto.custom_type,
                IsDistinct = dto.is_distinct,
                IsPublic = dto.is_public,
                MemberCount = dto.member_count,
                CreatedAt = dto.created_at,
                MyRole = MessageDtoMapper.ParseRole(dto.my_role),
                MyMemberState = ParseMemberState(dto.member_state),
                MyMutedState = dto.is_muted ? MutedStateBO.Muted : MutedStateBO.Unmuted,
                Members      = dto.members?.Select(ToMemberBO).ToList(),
                LastMessage  = MessageDtoMapper.ToBusinessObject(dto.last_message)
            };
        }

        /// <summary>
        /// Parse member state string to MemberStateBO enum.
        /// </summary>
        public static MemberStateBO ParseMemberState(string memberState)
        {
            if (string.IsNullOrEmpty(memberState))
                return MemberStateBO.None;

            switch (memberState.ToLowerInvariant())
            {
                case "invited":
                    return MemberStateBO.Invited;
                case "joined":
                    return MemberStateBO.Joined;
                case "left":
                    return MemberStateBO.Left;
                default:
                    return MemberStateBO.None;
            }
        }

        private static MemberBO ToMemberBO(MemberDTO dto) => new MemberBO
        {
            UserId      = dto.user_id,
            Nickname    = dto.nickname,
            ProfileUrl  = dto.profile_url,
            MemberState = ParseMemberState(dto.state),
            Role        = MessageDtoMapper.ParseRole(dto.role),
            IsMuted     = dto.is_muted,
        };

        /// <summary>
        /// Convert GroupChannelBO to ChannelDTO
        /// </summary>
        public static ChannelDTO ToDto(GroupChannelBO bo)
        {
            if (bo == null)
            {
                return null;
            }

            return new ChannelDTO
            {
                channel_url = bo.ChannelUrl,
                name = bo.Name,
                cover_url = bo.CoverUrl,
                custom_type = bo.CustomType,
                is_distinct = bo.IsDistinct,
                is_public = bo.IsPublic,
                member_count = bo.MemberCount,
                created_at = bo.CreatedAt,
                member_state = bo.MyMemberState.ToString().ToLowerInvariant(),
                is_muted = bo.MyMutedState == MutedStateBO.Muted
            };
        }
    }
}
