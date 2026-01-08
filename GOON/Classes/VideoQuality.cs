using System.Collections.Generic;
using System.Linq;

namespace GOON.Classes {
    /// <summary>
    /// Represents a video quality option with resolution and URL
    /// </summary>
    public class VideoQuality {
        public int Height { get; set; }
        public int Width { get; set; }
        public string Url { get; set; }
        public long Filesize { get; set; }
        public string Label => $"{Height}p";
        
        public VideoQuality(int height, string url, int width = 0, long filesize = 0) {
            Height = height;
            Url = url;
            Width = width;
            Filesize = filesize;
        }
    }

    /// <summary>
    /// Helper class for selecting the best quality video from multiple options
    /// </summary>
    public static class QualitySelector {
        /// <summary>
        /// Selects the highest quality video from a list of options
        /// Prioritizes: Height > Filesize (as tiebreaker)
        /// </summary>
        public static VideoQuality SelectBest(List<VideoQuality> qualities) {
            if (qualities == null || !qualities.Any()) return null;
            
            return qualities
                .Where(q => !string.IsNullOrEmpty(q.Url))
                .OrderByDescending(q => q.Height)
                .ThenByDescending(q => q.Filesize)
                .FirstOrDefault();
        }

        /// <summary>
        /// Detects quality/resolution from URL patterns
        /// </summary>
        public static int DetectQualityFromUrl(string url) {
            if (string.IsNullOrEmpty(url)) return 0;
            
            var urlLower = url.ToLowerInvariant();
            
            // Check for explicit quality markers
            if (urlLower.Contains("2160p") || urlLower.Contains("_2160") || urlLower.Contains("4k")) return 2160;
            if (urlLower.Contains("1440p") || urlLower.Contains("_1440") || urlLower.Contains("2k")) return 1440;
            if (urlLower.Contains("1080p") || urlLower.Contains("_1080") || urlLower.Contains("fhd")) return 1080;
            if (urlLower.Contains("720p") || urlLower.Contains("_720") || urlLower.Contains("hd")) return 720;
            if (urlLower.Contains("480p") || urlLower.Contains("_480") || urlLower.Contains("sd")) return 480;
            if (urlLower.Contains("360p") || urlLower.Contains("_360")) return 360;
            if (urlLower.Contains("240p") || urlLower.Contains("_240")) return 240;
            
            return 0; // Unknown quality
        }

        /// <summary>
        /// Detects quality from variable names (e.g., video_alt_url3 = 1080p)
        /// </summary>
        public static int DetectQualityFromVariableName(string variableName) {
            if (string.IsNullOrEmpty(variableName)) return 0;
            
            var nameLower = variableName.ToLowerInvariant();
            
            // Rule34Video patterns
            if (nameLower.Contains("video_alt_url3")) return 1080;
            if (nameLower.Contains("video_alt_url2")) return 720;
            if (nameLower.Contains("video_alt_url")) return 480;
            if (nameLower.Contains("video_url")) return 360;
            
            // Generic patterns
            if (nameLower.Contains("1080") || nameLower.Contains("fhd")) return 1080;
            if (nameLower.Contains("720") || nameLower.Contains("hd")) return 720;
            if (nameLower.Contains("480") || nameLower.Contains("sd")) return 480;
            if (nameLower.Contains("360")) return 360;
            
            return 0;
        }
    }
}
