namespace Gamania.GIMChat
{
    public class GimMember : GimUser
    {
        public GimMemberState MemberState { get; set; } = GimMemberState.None;
        public GimRole        Role        { get; set; } = GimRole.None;
        public bool           IsMuted     { get; set; }
    }
}
