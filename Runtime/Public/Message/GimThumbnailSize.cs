namespace Gamania.GIMChat
{
    /// <summary>
    /// Requested thumbnail dimensions for file message upload.
    /// Server generates actual thumbnails based on these constraints.
    /// </summary>
    public class GimThumbnailSize
    {
        /// <summary>Maximum width for the thumbnail.</summary>
        public int MaxWidth { get; set; }

        /// <summary>Maximum height for the thumbnail.</summary>
        public int MaxHeight { get; set; }

        public GimThumbnailSize() { }

        public GimThumbnailSize(int maxWidth, int maxHeight)
        {
            MaxWidth = maxWidth;
            MaxHeight = maxHeight;
        }
    }
}
