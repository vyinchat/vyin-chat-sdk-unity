using System;
using System.Collections.Generic;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Default implementation of IGimOpenChannelHandler with optional Action callbacks.
    /// </summary>
    public class GimOpenChannelHandler : IGimOpenChannelHandler
    {
        public Action<GimOpenChannel, GimUser> OnUserEnteredAction { get; set; }
        public Action<GimOpenChannel, GimUser> OnUserExitedAction { get; set; }
        public Action<GimOpenChannel> OnChannelUpdatedAction { get; set; }
        public Action<string> OnChannelDeletedAction { get; set; }
        public Action<GimOpenChannel> OnChannelFrozenAction { get; set; }
        public Action<GimOpenChannel> OnChannelUnfrozenAction { get; set; }
        public Action<IReadOnlyList<GimOpenChannel>> OnChannelParticipantCountChangedAction { get; set; }
        public Action<GimOpenChannel> OnOperatorUpdatedAction { get; set; }
        public Action<GimOpenChannel, GimUser> OnUserMutedAction { get; set; }
        public Action<GimOpenChannel, GimUser> OnUserUnmutedAction { get; set; }
        public Action<GimOpenChannel, GimUser> OnUserBannedAction { get; set; }
        public Action<GimOpenChannel, GimUser> OnUserUnbannedAction { get; set; }
        public Action<GimOpenChannel, GimBaseMessage> OnMessageReceivedAction { get; set; }
        public Action<GimOpenChannel, GimBaseMessage> OnMessageUpdatedAction { get; set; }
        public Action<GimOpenChannel, long> OnMessageDeletedAction { get; set; }

        public void OnUserEntered(GimOpenChannel channel, GimUser user) => OnUserEnteredAction?.Invoke(channel, user);
        public void OnUserExited(GimOpenChannel channel, GimUser user) => OnUserExitedAction?.Invoke(channel, user);
        public void OnChannelUpdated(GimOpenChannel channel) => OnChannelUpdatedAction?.Invoke(channel);
        public void OnChannelDeleted(string channelUrl) => OnChannelDeletedAction?.Invoke(channelUrl);
        public void OnChannelFrozen(GimOpenChannel channel) => OnChannelFrozenAction?.Invoke(channel);
        public void OnChannelUnfrozen(GimOpenChannel channel) => OnChannelUnfrozenAction?.Invoke(channel);
        public void OnChannelParticipantCountChanged(IReadOnlyList<GimOpenChannel> channels) => OnChannelParticipantCountChangedAction?.Invoke(channels);
        public void OnOperatorUpdated(GimOpenChannel channel) => OnOperatorUpdatedAction?.Invoke(channel);
        public void OnUserMuted(GimOpenChannel channel, GimUser user) => OnUserMutedAction?.Invoke(channel, user);
        public void OnUserUnmuted(GimOpenChannel channel, GimUser user) => OnUserUnmutedAction?.Invoke(channel, user);
        public void OnUserBanned(GimOpenChannel channel, GimUser user) => OnUserBannedAction?.Invoke(channel, user);
        public void OnUserUnbanned(GimOpenChannel channel, GimUser user) => OnUserUnbannedAction?.Invoke(channel, user);
        public void OnMessageReceived(GimOpenChannel channel, GimBaseMessage message) => OnMessageReceivedAction?.Invoke(channel, message);
        public void OnMessageUpdated(GimOpenChannel channel, GimBaseMessage message) => OnMessageUpdatedAction?.Invoke(channel, message);
        public void OnMessageDeleted(GimOpenChannel channel, long messageId) => OnMessageDeletedAction?.Invoke(channel, messageId);
    }
}
