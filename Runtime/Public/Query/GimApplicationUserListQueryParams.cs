using System.Collections.Generic;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Parameters for creating an ApplicationUserListQuery.
    /// </summary>
    public class GimApplicationUserListQueryParams
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

        /// <summary>
        /// Filter results to only include users whose nickname starts with this string.
        /// </summary>
        public string NicknameStartsWithFilter { get; set; }

        /// <summary>
        /// Filter results by metadata. Key is the metadata key, Values are the allowed values.
        /// </summary>
        public (string Key, List<string> Values)? MetaDataFilter { get; set; }
    }
}
