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

        /// <summary>
        /// Overloaded constructor for (url, height, label) pattern used in StashPatternExtractor
        /// </summary>
        public VideoQuality(string url, int height, string label = null) {
            Url = url;
            Height = height;
            Width = 0;
            Filesize = 0;
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
            
            var best = qualities
                .Where(q => !string.IsNullOrEmpty(q.Url))
                .OrderByDescending(q => q.Height)
                .ThenByDescending(q => q.Filesize)
                .FirstOrDefault();

            if (best != null) {
                Logger.Info($"[QualitySelector] Selected best: {best.Height}p (from {qualities.Count} options)");
            }

            return best;
        }

        /// <summary>
        /// Detects quality/resolution from URL patterns or other strings
        /// </summary>
        public static int DetectQualityFromUrl(string url) {
            return DetectQualityFromString(url);
        }

        /// <summary>
        /// Detects quality/resolution from any string containing resolution markers (e.g. "720p", "1080", "HD")
        /// </summary>
        public static int DetectQualityFromString(string text) {
            if (string.IsNullOrEmpty(text)) return 0;
            
            var lower = text.Trim().ToLowerInvariant();
            
            // Check for explicit numeric quality markers first (highest priority)
            if (lower.Contains("2160p") || lower.Contains("_2160") || lower == "2160") return 2160;
            if (lower.Contains("1440p") || lower.Contains("_1440") || lower == "1440") return 1440;
            if (lower.Contains("1080p") || lower.Contains("_1080") || lower == "1080") return 1080;
            if (lower.Contains("720p") || lower.Contains("_720") || lower == "720") return 720;
            if (lower.Contains("480p") || lower.Contains("_480") || lower == "480") return 480;
            if (lower.Contains("360p") || lower.Contains("_360") || lower == "360") return 360;
            if (lower.Contains("240p") || lower.Contains("_240") || lower == "240") return 240;
            if (lower.Contains("144p") || lower.Contains("_144") || lower == "144") return 144;
            
            // Check for named quality markers (second priority)
            if (lower.Contains("4k") || lower.Contains("uhd") || lower.Contains("ultra hd")) return 2160;
            if (lower.Contains("2k") || lower.Contains("qhd") || lower.Contains("quad hd")) return 1440;
            if (lower.Contains("fullhd") || lower.Contains("full hd") || lower.Contains("fhd")) return 1080;
            if (lower.Contains("hd") && !lower.Contains("sd")) return 720; // hd alone defaults to 720p
            if (lower.Contains("sd")) return 480;
            
            // Try parsing as raw number as a last resort
            if (int.TryParse(lower, out int res)) {
                if (new[] { 144, 240, 360, 480, 720, 1080, 1440, 2160, 4320 }.Contains(res)) {
                    return res;
                }
            }
            
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
            return DetectQualityFromString(nameLower);
        }
    }
}
