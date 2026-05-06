using System.Collections.Generic;

namespace Gamania.GIMChat.Internal.Domain.Models
{
    public class MessageBO
    {
        public long MessageId { get; set; }
        public string Message { get; set; }
        public string ChannelUrl { get; set; }
        public string ChannelType { get; set; }
        public long CreatedAt { get; set; }
        public bool Done { get; set; }
        public string CustomType { get; set; }
        public string Data { get; set; }
        public string ReqId { get; set; }
        public SenderBO Sender { get; set; }

        // File message fields (null/default when not a file message)
        public string FileUrl { get; set; }
        public string FileName { get; set; }
        public string FileMimeType { get; set; }
        public long FileSize { get; set; }
        public bool RequireAuth { get; set; }
        public string ObjectId { get; set; }
        public string ObjectStatus { get; set; }
        public List<ThumbnailBO> Thumbnails { get; set; }

        public bool IsFileMessage => !string.IsNullOrEmpty(FileUrl) || !string.IsNullOrEmpty(FileName) || !string.IsNullOrEmpty(ObjectId);
    }

    public class ThumbnailBO
    {
        public int MaxWidth { get; set; }
        public int MaxHeight { get; set; }
        public int RealWidth { get; set; }
        public int RealHeight { get; set; }
        public string Url { get; set; }
    }
}
