using System.Collections.Generic;

namespace Gamania.GIMChat.Internal.Data.DTOs
{
    /// <summary>
    /// Data Transfer Object for Open Channel API responses
    /// </summary>
    internal class OpenChannelDTO
    {
        public string channel_url { get; set; }
        public string name { get; set; }
        public string cover_url { get; set; }
        public string custom_type { get; set; }
        public string data { get; set; }
        public long created_at { get; set; }
        public bool is_frozen { get; set; }
        public int participant_count { get; set; }
        public List<UserDTO> operators { get; set; }
    }

    /// <summary>
    /// Data Transfer Object for User (operators, participants)
    /// </summary>
    internal class UserDTO
    {
        public string user_id { get; set; }
        public string nickname { get; set; }
        public string profile_url { get; set; }
    }

    /// <summary>
    /// Response for open channel list API
    /// </summary>
    internal class OpenChannelListResponseDTO
    {
        public List<OpenChannelDTO> channels { get; set; }
        public string next { get; set; }
    }

    /// <summary>
    /// Response for enter/exit open channel (WebSocket ACK)
    /// </summary>
    internal class OpenChannelEnterExitAckDTO
    {
        public int participant_count { get; set; }
    }

    /// <summary>
    /// Response for open channel participant list API
    /// </summary>
    internal class ParticipantListResponseDTO
    {
        public List<UserDTO> participants { get; set; }
        public string next { get; set; }
    }
}
