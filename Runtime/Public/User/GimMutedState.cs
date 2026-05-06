namespace Gamania.GIMChat
{
    /// <summary>
    /// Represents the muted state of the current user in a group channel.
    /// </summary>
    public enum GimMutedState
    {
        /// <summary>
        /// The user is not muted.
        /// </summary>
        Unmuted,

        /// <summary>
        /// The user is muted and cannot send messages.
        /// </summary>
        Muted
    }
}
