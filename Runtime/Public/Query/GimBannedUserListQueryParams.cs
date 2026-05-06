namespace Gamania.GIMChat
{
    /// <summary>
    /// Parameters for creating a BannedUserListQuery.
    /// </summary>
    public class GimBannedUserListQueryParams
    {
        private const int DefaultLimit = 20;

        /// <summary>
        /// The type of channel to query banned users from.
        /// </summary>
        public GimChannelType ChannelType { get; set; }

        /// <summary>
        /// The URL of the channel to query banned users from.
        /// </summary>
        public string ChannelUrl { get; set; }

        /// <summary>
        /// Maximum number of users to fetch per page.
        /// Default is 20.
        /// </summary>
        public int Limit { get; set; } = DefaultLimit;
    }
}
