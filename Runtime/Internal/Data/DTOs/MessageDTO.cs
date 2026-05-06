using System.Collections.Generic;

namespace Gamania.GIMChat.Internal.Data.DTOs
{
    public class MessageDTO
    {
        public long message_id { get; set; }
        public long msg_id { get; set; }  // alias of message_id
        public string message { get; set; }
        public string channel_url { get; set; }
        public string channel_type { get; set; }
        public long created_at { get; set; }
        public long ts { get; set; }  // alias of created_at
        public bool done { get; set; }
        public string custom_type { get; set; }
        public string data { get; set; }
        public string request_id { get; set; }
        public string req_id { get; set; }  // alias of request_id
        public SenderDTO user { get; set; }

        // File message fields
        public FileObjectDTO file { get; set; }
        public List<ThumbnailDTO> thumbnails { get; set; }
    }

    public class FileObjectDTO
    {
        public string url { get; set; }
        public string name { get; set; }
        public string type { get; set; }
        public long size { get; set; }
        public string object_id { get; set; }
        public string object_status { get; set; }
        public bool require_auth { get; set; }
    }

    public class ThumbnailDTO
    {
        public int width { get; set; }
        public int height { get; set; }
        public int real_width { get; set; }
        public int real_height { get; set; }
        public string url { get; set; }
    }
}
