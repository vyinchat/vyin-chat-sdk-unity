using System.Collections.Generic;

namespace Gamania.GIMChat.Internal.Data.DTOs
{
    /// <summary>
    /// Data Transfer Object for Channel API responses
    /// Maps to backend API JSON structure
    /// </summary>
    public class ChannelDTO
    {
        public string channel_url { get; set; }
        public string name { get; set; }
        public string cover_url { get; set; }
        public string custom_type { get; set; }
        public Dictionary<string, string> data { get; set; }
        public bool is_distinct { get; set; }
        public bool is_public { get; set; }
        public int member_count { get; set; }
        public long created_at { get; set; }
        public string my_role { get; set; }
        public string member_state { get; set; }
        public bool is_muted { get; set; }

        public List<MemberDTO> members { get; set; }
        public MessageDTO    last_message { get; set; }
    }
}
