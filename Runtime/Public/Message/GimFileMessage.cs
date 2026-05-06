using System.Collections.Generic;

namespace Gamania.GIMChat
{
    /// <summary>
    /// Represents a file message in a channel.
    /// Extends GimBaseMessage with file-specific properties.
    /// </summary>
    public class GimFileMessage : GimBaseMessage
    {
        /// <summary>
        /// File download URL with auth token appended if required.
        /// </summary>
        public string Url
        {
            get
            {
                if (RequireAuth && !string.IsNullOrEmpty(PlainUrl) && EKeyResolver != null)
                    return PlainUrl + "?auth=" + EKeyResolver();
                return PlainUrl;
            }
        }

        /// <summary>
        /// Internal resolver for the encryption key used in authenticated file URLs.
        /// Set during SDK initialization from the LOGI response eKey.
        /// </summary>
        internal static System.Func<string> EKeyResolver { get; set; }

        /// <summary>
        /// File download URL without auth token.
        /// </summary>
        public string PlainUrl { get; set; } = "";

        /// <summary>
        /// File name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// File size in bytes.
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// MIME type (e.g. "image/png", "application/pdf").
        /// </summary>
        public string MimeType { get; set; } = "";

        /// <summary>
        /// Server-generated thumbnails based on requested ThumbnailSizes.
        /// </summary>
        public List<GimThumbnail> Thumbnails { get; set; } = new List<GimThumbnail>();

        /// <summary>
        /// Whether the file URL requires an auth token to access.
        /// </summary>
        internal bool RequireAuth { get; set; }

        /// <summary>
        /// Server-assigned object ID from upload.
        /// </summary>
        internal string ObjectId { get; set; } = "";

        /// <summary>
        /// Server-side object processing status.
        /// </summary>
        internal string ObjectStatus { get; set; } = "";

        /// <summary>
        /// Original create params retained for resend.
        /// </summary>
        internal GimFileMessageCreateParams FileMessageCreateParams { get; set; }

        /// <summary>
        /// Creates a GimFileMessage from a GimBaseMessage, copying base fields.
        /// </summary>
        public static GimFileMessage FromBase(GimBaseMessage message)
        {
            if (message == null) return null;
            if (message is GimFileMessage fileMessage) return fileMessage;

            return new GimFileMessage
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
