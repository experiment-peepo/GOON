using System;
using System.Collections.Generic;
using System.Linq;
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
            if (string.IsNullOrEmpty(html)) return new List<VideoQuality>();

            var qualities = new List<VideoQuality>();

            try {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                
                // Extract from <source> tags
                var sourceNodes = doc.DocumentNode.SelectNodes("//video//source[@type='video/mp4']");
                if (sourceNodes != null) {
                    foreach (var source in sourceNodes) {
                        var url = source.GetAttributeValue("src", null);
                        if (!string.IsNullOrEmpty(url)) {
                            var quality = QualitySelector.DetectQualityFromUrl(url);
                            if (quality > 0) {
                                qualities.Add(new VideoQuality(quality, url));
                            }
                        }
                    }
                }

                // Extract from <video> src attribute
                var videoNode = doc.DocumentNode.SelectSingleNode("//video[@src]");
                if (videoNode != null) {
                    var url = videoNode.GetAttributeValue("src", null);
                    if (!string.IsNullOrEmpty(url)) {
                        var quality = QualitySelector.DetectQualityFromUrl(url);
                        qualities.Add(new VideoQuality(quality > 0 ? quality : 720, url));
                    }
                }

            } catch (Exception ex) {
                Logger.Warning($"Error extracting video sources: {ex.Message}");
            }

            return qualities.OrderByDescending(q => q.Height).ToList();
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
                    // Try to get attribute value first (for @content, @src, etc.)
                    if (xpath.Contains("/@")) {
                        var attrName = xpath.Substring(xpath.LastIndexOf("/@") + 2);
                        return node.GetAttributeValue(attrName, null);
                    }
                    
                    // Otherwise return inner text
                    return node.InnerText?.Trim();
                }
            } catch (Exception ex) {
                Logger.Warning($"Error extracting XPath {xpath}: {ex.Message}");
            }

            return null;
        }
    }
}
