namespace Gamania.GIMChat
{
    /// <summary>
    /// Represents a user message in a channel.
    /// </summary>
    public class GimUserMessage : GimBaseMessage
    {
        public static GimUserMessage FromBase(GimBaseMessage message)
        {
            if (message == null) return null;
            if (message is GimUserMessage userMessage) return userMessage;

            return new GimUserMessage
            {
                MessageId = message.MessageId,
                Message = message.Message,
                ChannelUrl = message.ChannelUrl,
                ChannelType = message.ChannelType,
                CreatedAt = message.CreatedAt,
                Done = message.Done,
                CustomType = message.CustomType,
                Data = message.Data,
                ReqId = message.ReqId,
                Sender = message.Sender,
                SendingStatus = message.SendingStatus,
                ErrorCode = message.ErrorCode
            };
        }
    }
}
