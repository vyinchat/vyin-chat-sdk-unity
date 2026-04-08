using Gamania.GIMChat.Internal;
using Gamania.GIMChat.Internal.Data.Cache;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Abstract base class for all GIMChat Collection types.
    ///
    /// Provides two shared services:
    /// - delegateManager → <see cref="DelegateManager"/>
    /// - dataSource      → <see cref="DataSource"/>
    ///
    /// Lifecycle management (dispose, GuardDisposed) is the responsibility
    /// of each concrete Collection subclass.
    /// </summary>
    public abstract class GimBaseCollection
    {
        /// <summary>
        /// Internal event routing hub.
        /// Concrete classes use this to subscribe/unsubscribe channel events.
        /// </summary>
        private protected readonly GimSdkDelegateManager DelegateManager
            = GimSdkDelegateManager.Instance;

        /// <summary>
        /// Shared in-memory cache for pending and failed messages.
        /// </summary>
        private protected readonly CacheDataSource DataSource
            = CacheDataSource.Instance;
    }
}
