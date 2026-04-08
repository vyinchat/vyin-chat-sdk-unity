namespace Gamania.GIMChat
{
    /// <summary>
    /// Parameters for updating user profile information.
    /// </summary>
    public class GimUserUpdateParams
    {
        /// <summary>
        /// New nickname. Set to null to keep unchanged.
        /// Set to empty string to clear.
        /// </summary>
        public string Nickname { get; set; }

        /// <summary>
        /// Profile image URL (read-only, use SetProfileImageUrl to set).
        /// </summary>
        public string ProfileImageUrl { get; private set; }

        /// <summary>
        /// Sets the profile image URL.
        /// </summary>
        /// <param name="url">The image URL.</param>
        /// <returns>This instance for method chaining.</returns>
        public GimUserUpdateParams SetProfileImageUrl(string url)
        {
            ProfileImageUrl = url;
            return this;
        }

        /// <summary>
        /// Checks if at least one field is set for update.
        /// </summary>
        internal bool HasAnyFieldSet()
        {
            return Nickname != null || ProfileImageUrl != null;
        }
    }
}
