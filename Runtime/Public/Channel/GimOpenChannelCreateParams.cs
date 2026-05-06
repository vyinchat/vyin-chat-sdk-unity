using System.Collections.Generic;

namespace Gamania.GIMChat
{
    public class GimOpenChannelCreateParams
    {
        public string Name { get; set; }
        public string ChannelUrl { get; set; }
        public string CoverUrl { get; set; }
        public string Data { get; set; }
        public string CustomType { get; set; }
        public List<string> OperatorUserIds { get; set; }
    }
}
