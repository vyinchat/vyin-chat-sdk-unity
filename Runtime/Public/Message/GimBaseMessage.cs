namespace Gamania.GIMChat
{
    /// <summary>
    /// Represents a message in a channel
    /// </summary>
    public class GimBaseMessage
    {
        /// <summary>
        /// Current sending status of this message.
        /// </summary>
        public GimSendingStatus SendingStatus { get; set; } = GimSendingStatus.None;

        /// <summary>
        /// Error code if the message failed to send. Null means no error.
        /// </summary>
        public GimErrorCode? ErrorCode { get; set; }

        /// <summary>
        /// Whether this message can be manually resent.
        /// </summary>
        public bool IsResendable => ErrorCode.HasValue && ErrorCode.Value.IsResendable();

        /// <summary>
        /// Unique identifier of the message
        /// </summary>
        public long MessageId { get; set; }

        /// <summary>
        /// Text content of the message
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// URL of the channel this message belongs to
        /// </summary>
        public string ChannelUrl { get; set; }

        /// <summary>
        /// Type of channel this message belongs to.
        /// Defaults to Group when not specified by the server.
        /// </summary>
        public GimChannelType ChannelType { get; set; } = GimChannelType.Group;

        /// <summary>
        /// Unix timestamp (milliseconds) when the message was created
        /// </summary>
        public long CreatedAt { get; set; }

        /// <summary>
        /// Indicates whether the streaming message is complete.
        /// Only applicable for streaming messages (e.g., AI responses).
        /// When true, this is the final version of the message.
        /// </summary>
        public bool Done { get; set; }

        /// <summary>
        /// Custom type for categorizing messages.
        /// Can be used to distinguish different message types (e.g., "text", "image", "ai_response").
        /// </summary>
        public string CustomType { get; set; }

        /// <summary>
        /// Additional custom data in JSON string format.
        /// Can be used to pass extra information with the message.
        /// </summary>
        public string Data { get; set; }

        /// <summary>
        /// Request ID for tracking message requests.
        /// </summary>
        public string ReqId { get; set; }

        /// <summary>
        /// Sender information including role.
        /// </summary>
        public GimSender Sender { get; set; }
    }
}
