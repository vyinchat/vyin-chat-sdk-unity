using System.Collections.Generic;
using System.Linq;

namespace Gamania.GIMChat.Internal.Domain.Message
{
    /// <summary>
    /// Upload part status.
    /// </summary>
    internal enum FilePartStatus
    {
        Scheduled,
        Succeeded
    }

    /// <summary>
    /// Represents a single upload part for multipart file upload.
    /// </summary>
    internal class FilePartEntry
    {
        public int PartNo { get; set; }
        public string SignedUrl { get; set; }
        public int PartSize { get; set; }
        public string ETag { get; set; }
        public long TimeExpired { get; set; }
        public FilePartStatus Status { get; set; } = FilePartStatus.Scheduled;
    }

    /// <summary>
    /// Upload session info for a single file message, tracking all parts.
    /// </summary>
    internal class FilePartInfo
    {
        public string RequestId { get; set; }
        public string ObjectId { get; set; }
        public string UploadId { get; set; }
        public List<FilePartEntry> Parts { get; set; } = new List<FilePartEntry>();

        public bool IsUploadedSuccessfully =>
            Parts.Count > 0 && Parts.All(p => p.Status == FilePartStatus.Succeeded);
    }

    /// <summary>
    /// In-memory cache for file upload parts, keyed by request ID.
    /// Thread-safe.
    /// </summary>
    internal class FilePartCache
    {
        private readonly Dictionary<string, FilePartInfo> _cache = new Dictionary<string, FilePartInfo>();
        private readonly object _lock = new object();

        public void Put(string requestId, FilePartInfo info)
        {
            lock (_lock)
            {
                _cache[requestId] = info;
            }
        }

        public FilePartInfo Get(string requestId)
        {
            lock (_lock)
            {
                return _cache.TryGetValue(requestId, out var info) ? info : null;
            }
        }

        public void Remove(string requestId)
        {
            lock (_lock)
            {
                _cache.Remove(requestId);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _cache.Clear();
            }
        }
    }
}
