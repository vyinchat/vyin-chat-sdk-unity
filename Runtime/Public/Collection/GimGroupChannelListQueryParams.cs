using System.Collections.Generic;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Parameters for querying group channel list.
    /// </summary>
    public class GimGroupChannelListQueryParams
    {
        /// <summary>
        /// Maximum number of channels to return per page.
        /// Default: 20, Max: 100
        /// </summary>
        public int Limit { get; set; } = 20;

        /// <summary>
        /// Filter channels matching any of the specified custom types.
        /// Maps to API query param: custom_types (comma-separated)
        /// </summary>
        public List<string> CustomTypesFilter { get; set; }

        /// <summary>
        /// Filter channels whose custom type starts with this prefix.
        /// Maps to API query param: custom_type_startswith
        /// </summary>
        public string CustomTypeStartsWithFilter { get; set; }

        /// <summary>
        /// Whether to include channels with no messages.
        /// Maps to API query param: show_empty (default: false)
        /// </summary>
        public bool IncludeEmpty { get; set; } = false;
    }
}
