using System.Collections.Generic;

namespace GOON.Classes {
    /// <summary>
    /// Video information extracted by yt-dlp
    /// </summary>
    public class YtDlpVideoInfo {
        public string Url { get; set; }
        public string Title { get; set; }
        public int Duration { get; set; }
        public string Thumbnail { get; set; }
        public List<YtDlpFormat> Formats { get; set; } = new List<YtDlpFormat>();
        public string Description { get; set; }
        public string Uploader { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
    }

    /// <summary>
    /// Video format/quality variant
    /// </summary>
    public class YtDlpFormat {
        public string FormatId { get; set; }
        public string Quality { get; set; }
        public string Url { get; set; }
        public long Filesize { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Extension { get; set; }
        public int? Fps { get; set; }
        
        public string DisplayName => $"{Quality} ({FormatSizeDisplay})";
        
        private string FormatSizeDisplay {
            get {
                if (Filesize <= 0) return "Unknown size";
                double mb = Filesize / 1024.0 / 1024.0;
                return mb >= 1024 ? $"{mb / 1024:F1} GB" : $"{mb:F0} MB";
            }
        }
    }
}
