namespace Gamania.GIMChat
{
    /// <summary>
    /// Represents the membership state of the current user in a group channel.
    /// </summary>
    public enum GimMemberState
    {
        /// <summary>
        /// The user has no membership state (default).
        /// </summary>
        None,

        /// <summary>
        /// The user has been invited but hasn't joined yet.
        /// </summary>
        Invited,

        /// <summary>
        /// The user has joined the channel.
        /// </summary>
        Joined,

        /// <summary>
        /// The user has left the channel.
        /// </summary>
        Left
    }
}
