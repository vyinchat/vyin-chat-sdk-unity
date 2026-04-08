using System;
using System.Collections.Generic;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Handler for receiving user-related events.
    /// Register via GimChat.AddUserEventHandler().
    /// </summary>
    public class GimUserEventHandler
    {
        /// <summary>
        /// Invoked when user information is updated.
        /// Called after UpdateCurrentUserInfo succeeds, or when
        /// user info changes are detected from incoming messages.
        /// </summary>
        public Action<IReadOnlyList<GimUser>> OnUserInfoUpdated;
    }
}
