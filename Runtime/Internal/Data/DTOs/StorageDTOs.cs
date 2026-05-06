using System.Collections.Generic;

namespace Gamania.GIMChat.Internal.Data.DTOs
{
    /// <summary>
    /// Response from POST /storage/file (single-part upload init).
    /// </summary>
    internal class SingleUploadInitResponseDTO
    {
        public string url { get; set; }
        public string object_id { get; set; }
        public Dictionary<string, string> fields { get; set; }
    }

    /// <summary>
    /// Response from POST /storage/file/multipart/initiate.
    /// </summary>
    internal class MultipartInitResponseDTO
    {
        public string object_id { get; set; }
        public string upload_id { get; set; }
        public List<SignPartDTO> sign_parts { get; set; }
    }

    internal class SignPartDTO
    {
        public string url { get; set; }
        public int part_no { get; set; }
    }

    /// <summary>
    /// Response from GET /storage/file/{objectId}/meta.
    /// </summary>
    internal class FileMetaResponseDTO
    {
        public string id { get; set; }
        public string filename { get; set; }
        public string mime { get; set; }
        public string status { get; set; }
    }
}
