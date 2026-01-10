using System.Collections.Generic;

namespace GOON.Classes {
    public class Playlist {
        public List<PlaylistItem> Items { get; set; } = new List<PlaylistItem>();
    }

    public class PlaylistItem {
        public string FilePath { get; set; }
        public string ScreenDeviceName { get; set; }
        public double Opacity { get; set; } = 0.9;
        public double Volume { get; set; } = 1.0;
        /// <summary>
        /// Optional title extracted from video page. If null or empty, VideoItem will use URL-based name extraction.
        /// </summary>
        public string Title { get; set; }
        /// <summary>
        /// The original page URL with full path (including slug). Used for re-extraction when video URLs expire.
        /// </summary>
        public string OriginalPageUrl { get; set; }
    }
}
