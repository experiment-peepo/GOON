using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace GOON.Classes {
    /// <summary>
    /// XPath-based HTML extractor inspired by Stash community scraper patterns
    /// Provides robust extraction with quality awareness
    /// </summary>
    public class StashPatternExtractor {
        /// <summary>
        /// Extracts Open Graph video URL (often highest quality)
        /// </summary>
        public static string ExtractOgVideo(string html) {
            if (string.IsNullOrEmpty(html)) return null;

            try {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                
                var node = doc.DocumentNode.SelectSingleNode("//meta[@property='og:video']/@content");
                return node?.GetAttributeValue("content", null);
            } catch (Exception ex) {
                Logger.Warning($"Error extracting og:video: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts Open Graph image/thumbnail URL
        /// </summary>
        public static string ExtractOgImage(string html) {
            if (string.IsNullOrEmpty(html)) return null;

            try {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                
                var node = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image']/@content");
                return node?.GetAttributeValue("content", null);
            } catch (Exception ex) {
                Logger.Warning($"Error extracting og:image: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts video title using Stash patterns for Hypnotube
        /// </summary>
        public static string ExtractHypnotubeTitle(string html) {
            if (string.IsNullOrEmpty(html)) return null;

            try {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                
                // Stash pattern: //div[@class='item-tr-inner-col inner-col']/h1/text()
                var node = doc.DocumentNode.SelectSingleNode("//div[@class='item-tr-inner-col inner-col']/h1");
                return node?.InnerText?.Trim();
            } catch (Exception ex) {
                Logger.Warning($"Error extracting Hypnotube title: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts all video source URLs from HTML and returns them sorted by quality
        /// </summary>
        public static List<VideoQuality> ExtractAllVideoSources(string html, string baseUrl = null) {
            var qualities = new List<VideoQuality>();
            if (string.IsNullOrEmpty(html)) return qualities;
            
            try {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                
                // 1. Try standard <video><source> tags
                var sourceNodes = doc.DocumentNode.SelectNodes("//video//source");
                if (sourceNodes != null) {
                    foreach (var source in sourceNodes) {
                        var url = source.GetAttributeValue("src", "");
                        if (string.IsNullOrEmpty(url)) continue;
                        
                        url = ResolveUrl(url, baseUrl);
                        
                        // Priority 1: Check URL for quality
                        var quality = QualitySelector.DetectQualityFromUrl(url);
                        
                        // Priority 2: Check attributes commonly used for quality
                        if (quality <= 0) {
                             var label = source.GetAttributeValue("label", "");
                             var title = source.GetAttributeValue("title", "");
                             var res = source.GetAttributeValue("res", "");
                             var dataRes = source.GetAttributeValue("data-res", "");
                             var sizes = source.GetAttributeValue("sizes", "");
                             var size = source.GetAttributeValue("size", "");
                             
                             quality = QualitySelector.DetectQualityFromString(label);
                             if (quality <= 0) quality = QualitySelector.DetectQualityFromString(title);
                             if (quality <= 0) quality = QualitySelector.DetectQualityFromString(res);
                             if (quality <= 0) quality = QualitySelector.DetectQualityFromString(dataRes);
                             if (quality <= 0) quality = QualitySelector.DetectQualityFromString(sizes);
                             if (quality <= 0) quality = QualitySelector.DetectQualityFromString(size);
                         }
                        
                         // Include everything
                        var detectedQuality = quality > 0 ? quality : 1;
                        Logger.Info($"[Extractor] Found <source> tag: {detectedQuality}p -> {url}");
                        qualities.Add(new VideoQuality(detectedQuality, url));
                    }
                }

                // 2. Try extracting from JSON sources in scripts
                var jsonSources = ExtractSourcesFromJson(html, baseUrl);
                foreach (var jsSource in jsonSources) {
                    if (!qualities.Any(q => q.Url == jsSource.Url)) {
                        Logger.Info($"[Extractor] Found JSON source: {jsSource.Height}p -> {jsSource.Url}");
                        qualities.Add(jsSource);
                    }
                }
                
                // 3. Extract from <video> src attribute (as fallback)
                var videoNode = doc.DocumentNode.SelectSingleNode("//video[@src]");
                if (videoNode != null) {
                    var url = videoNode.GetAttributeValue("src", null);
                    if (!string.IsNullOrEmpty(url)) {
                        url = ResolveUrl(url, baseUrl);
                        if (!qualities.Any(q => q.Url == url)) {
                            var quality = QualitySelector.DetectQualityFromUrl(url);
                            var detectedQuality = quality > 0 ? quality : 720;
                            Logger.Info($"[Extractor] Found fallback <video src>: {detectedQuality}p -> {url}");
                            qualities.Add(new VideoQuality(detectedQuality, url));
                        }
                    }
                }

            } catch (Exception ex) {
                Logger.Warning($"Error extracting video sources: {ex.Message}");
            }

            return qualities.OrderByDescending(q => q.Height).ToList();
        }

        private static List<VideoQuality> ExtractSourcesFromJson(string html, string baseUrl) {
            var sources = new List<VideoQuality>();
            if (string.IsNullOrWhiteSpace(html)) return sources;

            // Pattern for common JSON video source structures
            var jsonPatterns = new[] {
                @"\{[^}]*?src\s*:\s*['""]([^'""]+)['""][^}]*?size\s*:\s*(\d+)[^}]*?\}",
                @"\{[^}]*?size\s*:\s*(\d+)[^}]*?src\s*:\s*['""]([^'""]+)['""][^}]*?\}",
                @"[""']?label[""']?\s*:\s*[""'](\d+)[pP]?[""'][^}]*?[""']?src[""']?\s*:\s*[""']([^""']+)[""']",
                @"\\?[""']?src\\?[""']?\s*:\s*\\?[""']([^\\""']+\.mp4[^\\""']*)(\\?[""'])[^}]*?\\?[""']?size\\?[""']?\s*:\s*(\d+)",
                @"\s*[""']?(\d+)[pP]?[""']?\s*:\s*[""']([^""']+)[""']" // Simple "720": "url" map
            };

            foreach (var pattern in jsonPatterns) {
                var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);
                foreach (Match match in matches) {
                    if (match.Success && match.Groups.Count >= 3) {
                        try {
                            string url, label;
                            
                            if (int.TryParse(match.Groups[2].Value, out int q2)) {
                                url = match.Groups[1].Value.Replace("\\/", "/");
                                label = match.Groups[2].Value;
                            } else if (int.TryParse(match.Groups[1].Value, out int q1)) {
                                label = match.Groups[1].Value;
                                url = match.Groups[2].Value.Replace("\\/", "/");
                            } else if (match.Groups.Count >= 4 && int.TryParse(match.Groups[3].Value, out int q3)) { 
                                url = match.Groups[1].Value.Replace("\\/", "/");
                                label = match.Groups[3].Value;
                            } else {
                                continue;
                            }

                            if (!string.IsNullOrEmpty(url) && (url.Contains(".mp4") || url.Contains(".webm"))) {
                                var resolvedUrl = ResolveUrl(url, baseUrl);
                                var quality = QualitySelector.DetectQualityFromString(label);
                                if (quality > 0) {
                                    sources.Add(new VideoQuality(resolvedUrl, quality, $"{label}p"));
                                }
                            }
                        } catch { }
                    }
                }
            }

            return sources;
        }

        /// <summary>
        /// Extracts video tags using Stash patterns
        /// </summary>
        public static List<string> ExtractTags(string html, string xpath = "//div[@class='tags-block']/a") {
            if (string.IsNullOrEmpty(html)) return new List<string>();

            try {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                
                var tagNodes = doc.DocumentNode.SelectNodes(xpath);
                if (tagNodes != null) {
                    return tagNodes.Select(n => n.InnerText?.Trim())
                                  .Where(t => !string.IsNullOrEmpty(t))
                                  .ToList();
                }
            } catch (Exception ex) {
                Logger.Warning($"Error extracting tags: {ex.Message}");
            }

            return new List<string>();
        }

        /// <summary>
        /// Generic XPath extraction helper
        /// </summary>
        public static string ExtractByXPath(string html, string xpath) {
            if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(xpath)) return null;

            try {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                
                var node = doc.DocumentNode.SelectSingleNode(xpath);
                if (node != null) {
                    if (xpath.Contains("/@")) {
                        var attrName = xpath.Substring(xpath.LastIndexOf("/@") + 2);
                        return node.GetAttributeValue(attrName, null);
                    }
                    return node.InnerText?.Trim();
                }
            } catch (Exception ex) {
                Logger.Warning($"Error extracting XPath {xpath}: {ex.Message}");
            }

            return null;
        }

        private static string ResolveUrl(string url, string baseUrl) {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(baseUrl)) return url;
            if (url.StartsWith("http") || url.StartsWith("//")) {
                if (url.StartsWith("//")) {
                    var baseUri = new Uri(baseUrl);
                    return baseUri.Scheme + ":" + url;
                }
                return url;
            }
            try {
                var baseUri = new Uri(baseUrl);
                var absoluteUri = new Uri(baseUri, url);
                return absoluteUri.ToString();
            } catch {
                return url;
            }
        }
    }
}
