namespace Gamania.GIMChat.Internal.Data.DTOs
{
    public class MemberDTO
    {
        public string user_id      { get; set; }
        public string nickname     { get; set; }
        public string profile_url  { get; set; }
        public string state        { get; set; }
        public string role         { get; set; }
        public bool   is_muted     { get; set; }
    }
}
