using System.Collections.Generic;

namespace Gamania.GIMChat.Internal.Domain.Models
{
    public enum MemberStateBO
    {
        None,
        Invited,
        Joined,
        Left
    }

    public enum MutedStateBO
    {
        Unmuted,
        Muted
    }

    /// <summary>
    /// Channel Business Object (Domain Layer)
    /// Pure C# business entity, no external dependencies
    /// </summary>
    public class GroupChannelBO : BaseChannelBO
    {
        public bool IsDistinct { get; set; }
        public bool IsPublic { get; set; }
        public int MemberCount { get; set; }
        public RoleBO MyRole { get; set; } = RoleBO.None;
        public MemberStateBO MyMemberState { get; set; } = MemberStateBO.None;
        public MutedStateBO MyMutedState { get; set; } = MutedStateBO.Unmuted;

        public List<MemberBO> Members    { get; set; }
        public MessageBO      LastMessage { get; set; }
    }
}
