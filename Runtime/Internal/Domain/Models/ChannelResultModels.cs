using System.Collections.Generic;

namespace Gamania.GIMChat.Internal.Domain.Models
{
    internal class ChannelListResult
    {
        public IReadOnlyList<GroupChannelBO> Channels { get; set; }
        public string NextToken { get; set; }
    }

    internal class ChannelChangeLogResult
    {
        public IReadOnlyList<GroupChannelBO> UpdatedChannels { get; set; }
        public IReadOnlyList<string> DeletedChannelUrls { get; set; }
        public string NextToken { get; set; }
    }

    internal class OpenChannelListResult
    {
        public IReadOnlyList<OpenChannelBO> Channels { get; set; }
        public string NextToken { get; set; }
    }

    internal class ParticipantListResult
    {
        public IReadOnlyList<GimUser> Users { get; set; }
        public string NextToken { get; set; }
    }
}
