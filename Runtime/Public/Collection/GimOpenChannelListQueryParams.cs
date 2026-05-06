namespace Gamania.GIMChat
{
    /// <summary>
    /// Parameters for querying open channel list.
    /// </summary>
    public class GimOpenChannelListQueryParams
    {
        /// <summary>
        /// Maximum number of channels to return per page.
        /// Default: 20, Max: 100
        /// </summary>
        public int Limit { get; set; } = 20;

        /// <summary>
        /// Filter channels whose name contains this keyword.
        /// </summary>
        public string NameKeyword { get; set; }

        /// <summary>
        /// Filter channels whose URL contains this keyword.
        /// </summary>
        public string UrlKeyword { get; set; }

        /// <summary>
        /// Filter channels by exact custom type match.
        /// </summary>
        public string CustomTypeFilter { get; set; }

        /// <summary>
        /// Whether to include frozen channels in results.
        /// Default: true
        /// </summary>
        public bool IncludeFrozen { get; set; } = true;
    }
}
