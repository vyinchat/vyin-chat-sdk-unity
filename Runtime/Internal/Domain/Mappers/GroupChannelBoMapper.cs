using System.Collections.Generic;
using System.Linq;
using Gamania.GIMChat.Internal.Domain.Models;

namespace Gamania.GIMChat.Internal.Domain.Mappers
{
    /// <summary>
    /// Mapper for converting between GroupChannelBO and GimGroupChannel (Public API Model)
    /// Domain Layer responsibility: Business Object ↔ Public Model conversion
    /// Used by UseCases to convert internal BO to public-facing models
    /// </summary>
    public static class GroupChannelBoMapper
    {
        /// <summary>
        /// Convert GroupChannelBO to GimGroupChannel (Public API Model)
        /// </summary>
        public static GimGroupChannel ToPublicModel(GroupChannelBO bo)
        {
            if (bo == null)
            {
                return null;
            }

            return new GimGroupChannel
            {
                ChannelUrl = bo.ChannelUrl,
                Name = bo.Name,
                CoverUrl = bo.CoverUrl,
                CustomType = bo.CustomType,
                IsDistinct = bo.IsDistinct,
                IsPublic = bo.IsPublic,
                MemberCount = bo.MemberCount,
                CreatedAt = bo.CreatedAt,
                MyRole        = MessageBoMapper.ToPublicRole(bo.MyRole),
                MyMemberState = ToPublicMemberState(bo.MyMemberState),
                MyMutedState  = ToPublicMutedState(bo.MyMutedState),
                Members       = bo.Members?.Select(ToPublicMember).ToList(),
                LastMessage   = MessageBoMapper.ToPublicModel(bo.LastMessage)
            };
        }

        /// <summary>
        /// Convert GimGroupChannel to GroupChannelBO
        /// Used for input parameters (e.g., update operations)
        /// </summary>
        public static GroupChannelBO ToBusinessObject(GimGroupChannel model)
        {
            if (model == null)
            {
                return null;
            }

            return new GroupChannelBO
            {
                ChannelUrl = model.ChannelUrl,
                Name = model.Name,
                CoverUrl = model.CoverUrl,
                CustomType = model.CustomType,
                IsDistinct = model.IsDistinct,
                IsPublic = model.IsPublic,
                MemberCount = model.MemberCount,
                CreatedAt = model.CreatedAt,
                MyRole = MessageBoMapper.ToRoleBO(model.MyRole),
                MyMemberState = ToMemberStateBO(model.MyMemberState),
                MyMutedState = ToMutedStateBO(model.MyMutedState),
                LastMessage = MessageBoMapper.ToBusinessObject(model.LastMessage)
            };
        }

        private static GimMember ToPublicMember(MemberBO bo) => new GimMember
        {
            UserId      = bo.UserId,
            Nickname    = bo.Nickname,
            ProfileUrl  = bo.ProfileUrl,
            MemberState = ToPublicMemberState(bo.MemberState),
            Role        = MessageBoMapper.ToPublicRole(bo.Role),
            IsMuted     = bo.IsMuted,
        };

        public static GimMemberState ToPublicMemberState(MemberStateBO state)
        {
            switch (state)
            {
                case MemberStateBO.Invited:
                    return GimMemberState.Invited;
                case MemberStateBO.Joined:
                    return GimMemberState.Joined;
                case MemberStateBO.Left:
                    return GimMemberState.Left;
                default:
                    return GimMemberState.None;
            }
        }

        public static GimMutedState ToPublicMutedState(MutedStateBO state)
        {
            return state == MutedStateBO.Muted ? GimMutedState.Muted : GimMutedState.Unmuted;
        }

        public static MemberStateBO ToMemberStateBO(GimMemberState state)
        {
            switch (state)
            {
                case GimMemberState.Invited:
                    return MemberStateBO.Invited;
                case GimMemberState.Joined:
                    return MemberStateBO.Joined;
                case GimMemberState.Left:
                    return MemberStateBO.Left;
                default:
                    return MemberStateBO.None;
            }
        }

        public static MutedStateBO ToMutedStateBO(GimMutedState state)
        {
            return state == GimMutedState.Muted ? MutedStateBO.Muted : MutedStateBO.Unmuted;
        }
    }
}
