namespace Gamania.GIMChat
{
    /// <summary>
    /// Type of user restriction in a channel.
    /// </summary>
    public enum GimRestrictionType
    {
        /// <summary>User is muted and cannot send messages.</summary>
        Muted,

        /// <summary>User is banned and cannot access the channel.</summary>
        Banned
    }
}
