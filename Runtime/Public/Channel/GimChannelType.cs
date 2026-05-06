namespace Gamania.GIMChat
{
    /// <summary>
    /// The type of a channel.
    /// </summary>
    public enum GimChannelType
    {
        /// <summary>Group channel with private membership.</summary>
        Group,

        /// <summary>Open channel with public access.</summary>
        Open
    }

    internal static class GimChannelTypeExtensions
    {
        internal static string ToPathSegment(this GimChannelType channelType) => channelType switch
        {
            GimChannelType.Open  => "open_channels",
            _                    => "group_channels"
        };
    }
}
