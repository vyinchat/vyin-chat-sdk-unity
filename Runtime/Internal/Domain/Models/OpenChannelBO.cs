using System.Collections.Generic;

namespace Gamania.GIMChat.Internal.Domain.Models
{
    /// <summary>
    /// Open Channel Business Object (Domain Layer)
    /// </summary>
    internal class OpenChannelBO : BaseChannelBO
    {
        public string Data { get; set; }
        public bool IsFrozen { get; set; }
        public int ParticipantCount { get; set; }
        public List<UserBO> Operators { get; set; } = new List<UserBO>();
    }

    /// <summary>
    /// User Business Object for operators
    /// </summary>
    internal class UserBO
    {
        public string UserId { get; set; }
        public string Nickname { get; set; }
        public string ProfileUrl { get; set; }
    }
}
