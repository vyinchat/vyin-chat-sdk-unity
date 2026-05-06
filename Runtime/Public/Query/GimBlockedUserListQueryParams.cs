using System.Collections.Generic;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Parameters for creating a BlockedUserListQuery.
    /// </summary>
    public class GimBlockedUserListQueryParams
    {
        private const int DefaultLimit = 20;

        /// <summary>
        /// Maximum number of users to fetch per page.
        /// Default is 20.
        /// </summary>
        public int Limit { get; set; } = DefaultLimit;

        /// <summary>
        /// Filter results to only include users with these IDs.
        /// </summary>
        public List<string> UserIdsFilter { get; set; }
    }
}
