namespace Gamania.GIMChat
{
    /// <summary>
    /// Represents a server-generated thumbnail for a file message.
    /// </summary>
    public class GimThumbnail
    {
        /// <summary>Requested maximum width.</summary>
        public int MaxWidth { get; set; }

        /// <summary>Requested maximum height.</summary>
        public int MaxHeight { get; set; }

        /// <summary>Actual width after server processing. -1 if not yet available.</summary>
        public int RealWidth { get; set; } = -1;

        /// <summary>Actual height after server processing. -1 if not yet available.</summary>
        public int RealHeight { get; set; } = -1;

        /// <summary>Thumbnail URL without auth token.</summary>
        public string PlainUrl { get; set; } = "";

        /// <summary>Whether the thumbnail URL requires an auth token.</summary>
        internal bool RequireAuth { get; set; }

        /// <summary>
        /// Thumbnail URL with auth token appended if required.
        /// </summary>
        public string Url
        {
            get
            {
                if (RequireAuth && !string.IsNullOrEmpty(PlainUrl) && GimFileMessage.EKeyResolver != null)
                    return PlainUrl + "?auth=" + GimFileMessage.EKeyResolver();
                return PlainUrl;
            }
        }
    }
}
