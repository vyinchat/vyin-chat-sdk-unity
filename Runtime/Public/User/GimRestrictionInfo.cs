namespace Gamania.GIMChat
{
    /// <summary>
    /// Information about a user's restriction (mute or ban) in a channel.
    /// </summary>
    public class GimRestrictionInfo
    {
        /// <summary>
        /// Description of the restriction reason.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Unix timestamp (ms) when the restriction ends.
        /// -1 indicates an indefinite restriction.
        /// </summary>
        public long EndAt { get; }

        /// <summary>
        /// Type of restriction (Muted or Banned).
        /// </summary>
        public GimRestrictionType RestrictionType { get; }

        internal GimRestrictionInfo(string description, long endAt, GimRestrictionType restrictionType)
        {
            Description = description ?? string.Empty;
            EndAt = endAt;
            RestrictionType = restrictionType;
        }
    }
}
