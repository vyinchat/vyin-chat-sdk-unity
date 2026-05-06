using System.Collections.Generic;

namespace Gamania.GIMChat.Internal.Domain.Models
{
    internal class RestrictedUserBO
    {
        public string UserId { get; set; }
        public string Nickname { get; set; }
        public string ProfileUrl { get; set; }
        public string Description { get; set; }
        public long EndAt { get; set; }
    }

    internal class RestrictedUserListResult
    {
        public List<RestrictedUserBO> Users { get; set; }
        public string NextToken { get; set; }
    }
}
