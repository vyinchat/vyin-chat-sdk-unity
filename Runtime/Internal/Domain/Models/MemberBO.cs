namespace Gamania.GIMChat.Internal.Domain.Models
{
    public class MemberBO
    {
        public string       UserId      { get; set; }
        public string       Nickname    { get; set; }
        public string       ProfileUrl  { get; set; }
        public MemberStateBO MemberState { get; set; } = MemberStateBO.None;
        public RoleBO        Role        { get; set; } = RoleBO.None;
        public bool          IsMuted     { get; set; }
    }
}
