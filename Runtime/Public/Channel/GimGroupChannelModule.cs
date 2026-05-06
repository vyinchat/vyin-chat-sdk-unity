using System;
using System.Threading.Tasks;

namespace Gamania.GIMChat
{
    /// <summary>
    /// [DEPRECATED] Use static methods on <see cref="GimGroupChannel"/> instead.
    /// </summary>
    [Obsolete("Use GimGroupChannel static methods instead. GimGroupChannelModule will be removed in a future version.")]
    public static class GimGroupChannelModule
    {
        [Obsolete("Use GimGroupChannel.GetChannelAsync instead")]
        public static Task<GimGroupChannel> GetGroupChannelAsync(string channelUrl)
            => GimGroupChannel.GetChannelAsync(channelUrl);

        [Obsolete("Use GimGroupChannel.GetChannel instead")]
        public static void GetGroupChannel(string channelUrl, GimGroupChannelCallbackHandler callback)
            => GimGroupChannel.GetChannel(channelUrl, callback);

        [Obsolete("Use GimGroupChannel.CreateChannelAsync instead")]
        public static Task<GimGroupChannel> CreateGroupChannelAsync(GimGroupChannelCreateParams createParams)
            => GimGroupChannel.CreateChannelAsync(createParams);

        [Obsolete("Use GimGroupChannel.CreateChannel instead")]
        public static void CreateGroupChannel(GimGroupChannelCreateParams createParams, GimGroupChannelCallbackHandler callback)
            => GimGroupChannel.CreateChannel(createParams, callback);

        [Obsolete("Use GimGroupChannel.CreateChannelAsync or CreateChannel with GimGroupChannelCallbackHandler instead")]
        public static void CreateGroupChannel(GimGroupChannelCreateParams channelCreateParams, Action<string, string> callback)
            => GimGroupChannel.CreateGroupChannel(channelCreateParams, callback);
    }
}
