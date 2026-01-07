using System;

namespace GOON.Classes {
    /// <summary>
    /// Holds extracted metadata for a video
    /// </summary>
    public class VideoMetadata {
        public string Url { get; set; }
        public string Title { get; set; }
        public string SourcePage { get; set; }
        
        public bool IsValid => !string.IsNullOrEmpty(Url);

        public VideoMetadata() { }

        public VideoMetadata(string url, string title, string sourcePage = null) {
            Url = url;
            Title = title;
            SourcePage = sourcePage;
        }
    }
}
