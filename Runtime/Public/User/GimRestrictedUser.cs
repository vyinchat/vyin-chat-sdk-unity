namespace Gamania.GIMChat
{
    /// <summary>
    /// Represents a user with restriction information (muted or banned).
    /// </summary>
    public class GimRestrictedUser : GimUser
    {
        /// <summary>
        /// Information about the restriction applied to this user.
        /// </summary>
        public GimRestrictionInfo RestrictionInfo { get; }

        internal GimRestrictedUser(
            string userId,
            string nickname,
            string profileUrl,
            GimRestrictionInfo restrictionInfo)
        {
            UserId = userId;
            Nickname = nickname;
            ProfileUrl = profileUrl;
            RestrictionInfo = restrictionInfo;
        }
    }
}
