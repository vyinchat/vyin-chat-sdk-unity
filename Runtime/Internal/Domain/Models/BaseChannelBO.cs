namespace Gamania.GIMChat.Internal.Domain.Models
{
    /// <summary>
    /// Base Channel Business Object (Domain Layer).
    /// Holds properties shared by both group and open channels.
    /// </summary>
    public abstract class BaseChannelBO
    {
        public string ChannelUrl { get; set; }
        public string Name { get; set; }
        public string CoverUrl { get; set; }
        public string CustomType { get; set; }
        public long CreatedAt { get; set; }
    }
}
