using System.Collections.Generic;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Interface for receiving open channel events.
    /// Events are only received for channels that have been entered.
    /// </summary>
    public interface IGimOpenChannelHandler
    {
        void OnUserEntered(GimOpenChannel channel, GimUser user);
        void OnUserExited(GimOpenChannel channel, GimUser user);
        void OnChannelUpdated(GimOpenChannel channel);
        void OnChannelDeleted(string channelUrl);
        void OnChannelFrozen(GimOpenChannel channel);
        void OnChannelUnfrozen(GimOpenChannel channel);
        void OnChannelParticipantCountChanged(IReadOnlyList<GimOpenChannel> channels);
        void OnOperatorUpdated(GimOpenChannel channel);
        void OnUserMuted(GimOpenChannel channel, GimUser user);
        void OnUserUnmuted(GimOpenChannel channel, GimUser user);
        void OnUserBanned(GimOpenChannel channel, GimUser user);
        void OnUserUnbanned(GimOpenChannel channel, GimUser user);
        void OnMessageReceived(GimOpenChannel channel, GimBaseMessage message);
        void OnMessageUpdated(GimOpenChannel channel, GimBaseMessage message);
        void OnMessageDeleted(GimOpenChannel channel, long messageId);
    }
}
