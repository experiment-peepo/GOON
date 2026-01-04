using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TrainMeX.Classes {
    /// <summary>
    /// Service for extracting direct video URLs from page URLs
    /// </summary>
    public class VideoUrlExtractor {
        private readonly IHtmlFetcher _htmlFetcher;
        private readonly LruCache<string, string> _urlCache;

        public VideoUrlExtractor(IHtmlFetcher htmlFetcher = null) {
            _htmlFetcher = htmlFetcher ?? new StandardHtmlFetcher();
            var ttl = TimeSpan.FromMinutes(Constants.UrlCacheTtlMinutes);
            _urlCache = new LruCache<string, string>(Constants.MaxFileCacheSize, ttl);
        }

        /// <summary>
        /// Extracts a direct video URL from a page URL
        /// </summary>
        /// <param name="pageUrl">The page URL to extract from</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The direct video URL, or null if extraction failed</returns>
        public async Task<string> ExtractVideoUrlAsync(string pageUrl, CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(pageUrl)) return null;
            
            // Check cache first
            if (_urlCache.TryGetValue(pageUrl, out string cachedUrl)) {
                return cachedUrl;
            }

            try {
                // Normalize URL
                var normalizedUrl = FileValidator.NormalizeUrl(pageUrl);
                
                // Determine site and extract accordingly
                var uri = new Uri(normalizedUrl);
                var host = uri.Host.ToLowerInvariant();
                
                // If it's already a direct video URL, return it immediately
                if (Constants.VideoExtensions.Any(ext => uri.AbsolutePath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) {
                    return normalizedUrl;
                }
                
                string videoUrl = null;
                
                if (host.Contains("hypnotube.com")) {
                    videoUrl = await ExtractHypnotubeUrlAsync(normalizedUrl, cancellationToken);
                } else if (host.Contains("iwara.tv")) {
                    videoUrl = await ExtractIwaraUrlAsync(normalizedUrl, cancellationToken);
                } else if (host.Contains("rule34video.com")) {
                    videoUrl = await ExtractRule34VideoUrlAsync(normalizedUrl, cancellationToken);
                } else if (host.Contains("pmvhaven.com")) {
                    videoUrl = await ExtractPmvHavenUrlAsync(normalizedUrl, cancellationToken);
                } else {
                    // Generic extraction for other sites
                    videoUrl = await ExtractGenericVideoUrlAsync(normalizedUrl, cancellationToken);
                }

                // Cache the result if successful
                if (videoUrl != null) {
                    _urlCache.Set(pageUrl, videoUrl);
                }

                return videoUrl;
            } catch (Exception ex) {
                Logger.Error($"Error extracting video URL from {pageUrl}", ex);
                return null;
            }
        }

        private async Task<string> ExtractHypnotubeUrlAsync(string url, CancellationToken cancellationToken) {
            try {
                var html = await FetchHtmlAsync(url, cancellationToken);
                if (string.IsNullOrWhiteSpace(html)) return null;

                // Look for video source in HTML
                // Hypnotube typically uses video tags or source elements
                var videoUrl = ExtractVideoFromHtml(html, url);
                return videoUrl;
            } catch (Exception ex) {
                Logger.Warning($"Error extracting Hypnotube URL: {ex.Message}");
                return null;
            }
        }

        private async Task<string> ExtractIwaraUrlAsync(string url, CancellationToken cancellationToken) {
            try {
                var html = await FetchHtmlAsync(url, cancellationToken);
                if (string.IsNullOrWhiteSpace(html)) return null;

                // Iwara.tv uses specific video player structure
                // Look for video source in page
                var videoUrl = ExtractVideoFromHtml(html, url);
                
                // Iwara may also have video URLs in JSON data
                if (videoUrl == null) {
                    videoUrl = ExtractVideoFromJson(html);
                }
                
                return videoUrl;
            } catch (Exception ex) {
                Logger.Warning($"Error extracting Iwara URL: {ex.Message}");
                return null;
            }
        }

        private async Task<string> ExtractRule34VideoUrlAsync(string url, CancellationToken cancellationToken) {
            try {
                var html = await FetchHtmlAsync(url, cancellationToken);
                if (string.IsNullOrWhiteSpace(html)) return null;

                var videoUrl = ExtractVideoFromHtml(html, url);
                return videoUrl;
            } catch (Exception ex) {
                Logger.Warning($"Error extracting RULE34Video URL: {ex.Message}");
                return null;
            }
        }

        private async Task<string> ExtractPmvHavenUrlAsync(string url, CancellationToken cancellationToken) {
            try {
                var html = await FetchHtmlAsync(url, cancellationToken);
                if (string.IsNullOrWhiteSpace(html)) return null;

                var videoUrl = ExtractVideoFromHtml(html, url);
                return videoUrl;
            } catch (Exception ex) {
                Logger.Warning($"Error extracting PMVHaven URL: {ex.Message}");
                return null;
            }
        }

        private async Task<string> ExtractGenericVideoUrlAsync(string url, CancellationToken cancellationToken) {
            try {
                var html = await FetchHtmlAsync(url, cancellationToken);
                if (string.IsNullOrWhiteSpace(html)) return null;

                var videoUrl = ExtractVideoFromHtml(html, url);
                return videoUrl;
            } catch (Exception ex) {
                Logger.Warning($"Error extracting generic video URL: {ex.Message}");
                return null;
            }
        }

        private async Task<string> FetchHtmlAsync(string url, CancellationToken cancellationToken) {
            return await _htmlFetcher.FetchHtmlAsync(url, cancellationToken);
        }

        private string ExtractVideoFromHtml(string html, string baseUrl) {
            if (string.IsNullOrWhiteSpace(html)) return null;

            try {
                // Method 1: Look for <video> tags with src attribute
                var videoSrcPattern = @"<video[^>]+src\s*=\s*[""']([^""']+)[""']";
                var match = Regex.Match(html, videoSrcPattern, RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 1) {
                    var videoUrl = match.Groups[1].Value;
                    return ResolveUrl(videoUrl, baseUrl);
                }

                // Method 2: Look for <source> tags within video elements
                var sourcePattern = @"<source[^>]+src\s*=\s*[""']([^""']+)[""']";
                var sourceMatches = Regex.Matches(html, sourcePattern, RegexOptions.IgnoreCase);
                foreach (Match sourceMatch in sourceMatches) {
                    if (sourceMatch.Success && sourceMatch.Groups.Count > 1) {
                        var videoUrl = sourceMatch.Groups[1].Value;
                        // Prefer video files with common extensions
                        if (Constants.VideoExtensions.Any(ext => videoUrl.ToLowerInvariant().EndsWith(ext))) {
                            return ResolveUrl(videoUrl, baseUrl);
                        }
                    }
                }

                // Method 3: Look for video URLs in JavaScript variables
                var jsVideoPatterns = new[] {
                    @"(?:src|url|source|videoUrl|file)\s*[:=]\s*[""']([^""']*\.(?:mp4|webm|mkv|avi|mov|wmv|m4v|mpg|mpeg|ts|m2ts)[^""']*)[""']",
                    @"[""']([^""']*\.(?:mp4|webm|mkv|avi|mov|wmv|m4v|mpg|mpeg|ts|m2ts)[^""']*)[""']"
                };

                foreach (var pattern in jsVideoPatterns) {
                    var jsMatches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);
                    foreach (Match jsMatch in jsMatches) {
                        if (jsMatch.Success && jsMatch.Groups.Count > 1) {
                            var videoUrl = jsMatch.Groups[1].Value;
                            if (IsValidVideoUrl(videoUrl)) {
                                return ResolveUrl(videoUrl, baseUrl);
                            }
                        }
                    }
                }

                return null;
            } catch (Exception ex) {
                Logger.Warning($"Error extracting video from HTML: {ex.Message}");
                return null;
            }
        }

        private string ExtractVideoFromJson(string html) {
            if (string.IsNullOrWhiteSpace(html)) return null;

            try {
                // Look for JSON objects that might contain video URLs
                var jsonPattern = @"\{[^{}]*""(?:src|url|source|file|videoUrl)""\s*:\s*""([^""]+\.(?:mp4|webm|mkv|avi|mov|wmv|m4v|mpg|mpeg|ts|m2ts)[^""]*)""[^{}]*\}";
                var matches = Regex.Matches(html, jsonPattern, RegexOptions.IgnoreCase);
                
                foreach (Match match in matches) {
                    if (match.Success && match.Groups.Count > 1) {
                        var videoUrl = match.Groups[1].Value;
                        if (IsValidVideoUrl(videoUrl)) {
                            return videoUrl;
                        }
                    }
                }

                return null;
            } catch (Exception ex) {
                Logger.Warning($"Error extracting video from JSON: {ex.Message}");
                return null;
            }
        }

        private string ResolveUrl(string url, string baseUrl) {
            if (string.IsNullOrWhiteSpace(url)) return null;

            try {
                if (Uri.TryCreate(url, UriKind.Absolute, out Uri absoluteUri)) {
                    return absoluteUri.ToString();
                }

                if (Uri.TryCreate(new Uri(baseUrl), url, out Uri resolvedUri)) {
                    return resolvedUri.ToString();
                }

                return url;
            } catch {
                return url;
            }
        }

        private bool IsValidVideoUrl(string url) {
            if (string.IsNullOrWhiteSpace(url)) return false;
            
            // Check if it's a valid URL with video extension
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri uri)) {
                var path = uri.AbsolutePath.ToLowerInvariant();
                return Constants.VideoExtensions.Any(ext => path.EndsWith(ext)) ||
                       uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
            }

            return false;
        }

        /// <summary>
        /// Extracts video title from a page URL using multiple methods
        /// </summary>
        /// <param name="pageUrl">The page URL to extract title from</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The video title, or null if extraction failed</returns>
        public async Task<string> ExtractVideoTitleAsync(string pageUrl, CancellationToken cancellationToken = default) {
            if (string.IsNullOrWhiteSpace(pageUrl)) return null;

            try {
                var normalizedUrl = FileValidator.NormalizeUrl(pageUrl);
                var html = await FetchHtmlAsync(normalizedUrl, cancellationToken);
                if (string.IsNullOrWhiteSpace(html)) return null;

                // Method 1: Try Open Graph meta tags
                try {
                    var ogTitle = ExtractMetaTag(html, "og:title");
                    if (!string.IsNullOrWhiteSpace(ogTitle)) {
                        var sanitized = SanitizeTitle(ogTitle);
                        if (!string.IsNullOrWhiteSpace(sanitized)) {
                            return sanitized;
                        }
                    }
                } catch (Exception ex) {
                    Logger.Warning($"Error extracting og:title from {pageUrl}: {ex.Message}");
                }

                // Method 2: Try Twitter meta tag
                try {
                    var twitterTitle = ExtractMetaTag(html, "twitter:title");
                    if (!string.IsNullOrWhiteSpace(twitterTitle)) {
                        var sanitized = SanitizeTitle(twitterTitle);
                        if (!string.IsNullOrWhiteSpace(sanitized)) {
                            return sanitized;
                        }
                    }
                } catch (Exception ex) {
                    Logger.Warning($"Error extracting twitter:title from {pageUrl}: {ex.Message}");
                }

                // Method 3: Try HTML title tag
                try {
                    var htmlTitle = ExtractHtmlTitle(html);
                    if (!string.IsNullOrWhiteSpace(htmlTitle)) {
                        var sanitized = SanitizeTitle(htmlTitle);
                        if (!string.IsNullOrWhiteSpace(sanitized)) {
                            return sanitized;
                        }
                    }
                } catch (Exception ex) {
                    Logger.Warning($"Error extracting HTML title from {pageUrl}: {ex.Message}");
                }

                // Method 4: Try page elements (site-specific)
                try {
                    var elementTitle = ExtractTitleFromPageElements(html, normalizedUrl);
                    if (!string.IsNullOrWhiteSpace(elementTitle)) {
                        var sanitized = SanitizeTitle(elementTitle);
                        if (!string.IsNullOrWhiteSpace(sanitized)) {
                            return sanitized;
                        }
                    }
                } catch (Exception ex) {
                    Logger.Warning($"Error extracting title from page elements from {pageUrl}: {ex.Message}");
                }

                return null;
            } catch (Exception ex) {
                Logger.Warning($"Error extracting video title from {pageUrl}: {ex.Message}");
                return null;
            }
        }

        private string ExtractMetaTag(string html, string propertyName) {
            if (string.IsNullOrWhiteSpace(html)) return null;

            try {
                var pattern = $@"<meta\s+[^>]*(?:property|name)\s*=\s*[""']{Regex.Escape(propertyName)}[""'][^>]*content\s*=\s*[""']([^""']+)[""']";
                var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 1) {
                    return WebUtility.HtmlDecode(match.Groups[1].Value);
                }

                // Alternative pattern: content before property/name
                pattern = $@"<meta\s+[^>]*content\s*=\s*[""']([^""']+)[""'][^>]*(?:property|name)\s*=\s*[""']{Regex.Escape(propertyName)}[""']";
                match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 1) {
                    return WebUtility.HtmlDecode(match.Groups[1].Value);
                }
            } catch (Exception ex) {
                Logger.Warning($"Error extracting meta tag {propertyName}: {ex.Message}");
            }

            return null;
        }

        private string ExtractHtmlTitle(string html) {
            if (string.IsNullOrWhiteSpace(html)) return null;

            try {
                var titlePattern = @"<title[^>]*>([^<]+)</title>";
                var match = Regex.Match(html, titlePattern, RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 1) {
                    var title = WebUtility.HtmlDecode(match.Groups[1].Value);
                    
                    // Clean up common site suffixes
                    var suffixes = new[] { " - Hypnotube", " | Hypnotube", " - RULE34VIDEO", " | RULE34VIDEO", 
                                          " - PMVHaven", " | PMVHaven", " - Iwara", " | Iwara" };
                    foreach (var suffix in suffixes) {
                        if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
                            title = title.Substring(0, title.Length - suffix.Length).Trim();
                            break;
                        }
                    }
                    
                    return title;
                }
            } catch (Exception ex) {
                Logger.Warning($"Error extracting HTML title: {ex.Message}");
            }

            return null;
        }

        private string ExtractTitleFromPageElements(string html, string url) {
            if (string.IsNullOrWhiteSpace(html)) return null;

            try {
                var uri = new Uri(url);
                var host = uri.Host.ToLowerInvariant();

                // Site-specific extraction patterns
                if (host.Contains("hypnotube.com")) {
                    // Try h1 with video-title class or similar
                    var pattern = @"<h1[^>]*class\s*=\s*[""'][^""']*video[^""']*title[^""']*[""'][^>]*>([^<]+)</h1>";
                    var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                    if (match.Success && match.Groups.Count > 1) {
                        return WebUtility.HtmlDecode(match.Groups[1].Value);
                    }
                } else if (host.Contains("rule34video.com")) {
                    // Try common title patterns
                    var patterns = new[] {
                        @"<h1[^>]*>([^<]+)</h1>",
                        @"<div[^>]*class\s*=\s*[""'][^""']*title[^""']*[""'][^>]*>([^<]+)</div>"
                    };
                    foreach (var pattern in patterns) {
                        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                        if (match.Success && match.Groups.Count > 1) {
                            var title = WebUtility.HtmlDecode(match.Groups[1].Value);
                            if (!string.IsNullOrWhiteSpace(title) && title.Length > 3) {
                                return title;
                            }
                        }
                    }
                } else if (host.Contains("pmvhaven.com")) {
                    // Try h1 or title div
                    var patterns = new[] {
                        @"<h1[^>]*>([^<]+)</h1>",
                        @"<div[^>]*class\s*=\s*[""'][^""']*video[^""']*title[^""']*[""'][^>]*>([^<]+)</div>"
                    };
                    foreach (var pattern in patterns) {
                        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                        if (match.Success && match.Groups.Count > 1) {
                            var title = WebUtility.HtmlDecode(match.Groups[1].Value);
                            if (!string.IsNullOrWhiteSpace(title) && title.Length > 3) {
                                return title;
                            }
                        }
                    }
                } else if (host.Contains("iwara.tv")) {
                    // Iwara specific patterns
                    var pattern = @"<h1[^>]*class\s*=\s*[""'][^""']*title[^""']*[""'][^>]*>([^<]+)</h1>";
                    var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                    if (match.Success && match.Groups.Count > 1) {
                        return WebUtility.HtmlDecode(match.Groups[1].Value);
                    }
                }

                // Generic fallback: try first h1
                var genericPattern = @"<h1[^>]*>([^<]+)</h1>";
                var genericMatch = Regex.Match(html, genericPattern, RegexOptions.IgnoreCase);
                if (genericMatch.Success && genericMatch.Groups.Count > 1) {
                    var title = WebUtility.HtmlDecode(genericMatch.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(title) && title.Length > 3) {
                        return title;
                    }
                }
            } catch (Exception ex) {
                Logger.Warning($"Error extracting title from page elements: {ex.Message}");
            }

            return null;
        }

        private string SanitizeTitle(string title) {
            if (string.IsNullOrWhiteSpace(title)) return null;

            try {
                // Remove HTML tags
                title = Regex.Replace(title, @"<[^>]+>", string.Empty);
                
                // Decode HTML entities
                title = WebUtility.HtmlDecode(title);
                
                // Trim whitespace
                title = title.Trim();
                
                // Limit length (reasonable max for display)
                if (title.Length > 200) {
                    title = title.Substring(0, 197) + "...";
                }
                
                // Remove excessive whitespace
                title = Regex.Replace(title, @"\s+", " ");
                
                return string.IsNullOrWhiteSpace(title) ? null : title;
            } catch (Exception ex) {
                Logger.Warning($"Error sanitizing title: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Clears the URL cache
        /// </summary>
        public void ClearCache() {
            _urlCache.Clear();
        }
    }
}



