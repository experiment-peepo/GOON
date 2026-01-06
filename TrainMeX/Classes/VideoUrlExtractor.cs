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

                Logger.Info($"ExtractHypnotubeUrlAsync: Processing HTML from {url}");

                // Hypnotube often has the video URL in a script or data attribute
                // but we start with a generic extraction which is quite powerful now
                Logger.Info("Hypnotube: Trying generic extraction");
                var videoUrl = ExtractVideoFromHtml(html, url);
                
                if (videoUrl != null) return videoUrl;

                // Fallback site-specific: Hypnotube often uses a player config
                var playerPattern = @"(?:video_url|file|src)\s*[:=]\s*[""']([^""']+)[""']";
                var match = Regex.Match(html, playerPattern, RegexOptions.IgnoreCase);
                if (match.Success) {
                    return ResolveUrl(match.Groups[1].Value, url);
                }

                return null;
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
                    videoUrl = ExtractVideoFromJson(html, url);
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

                Logger.Info("ExtractRule34VideoUrlAsync: Starting specialized extraction");

                // 1. Extract 'rnd' token from flashvars
                var rndPattern = @"rnd\s*:\s*['""](\d+)['""]";
                var rndMatch = Regex.Match(html, rndPattern, RegexOptions.IgnoreCase);
                string rndToken = rndMatch.Success ? rndMatch.Groups[1].Value : null;
                
                if (!string.IsNullOrEmpty(rndToken)) {
                    Logger.Info($"Rule34Video: Found rnd token: {rndToken}");
                } else {
                    Logger.Warning("Rule34Video: could not find rnd token, URL might fail");
                }

                // 2. Extract best video URL from variables
                // Priorities: video_alt_url3 (1080p) > video_alt_url2 (720p) > video_alt_url (480p) > video_url (360p)
                var priorities = new[] { "video_alt_url3", "video_alt_url2", "video_alt_url", "video_url" };
                string bestUrl = null;

                foreach (var key in priorities) {
                    var pattern = $@"{key}\s*:\s*['""]([^'""]+)['""]";
                    var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase);
                    if (match.Success && match.Groups.Count > 1) {
                        var rawUrl = match.Groups[1].Value;
                        if (!string.IsNullOrWhiteSpace(rawUrl) && rawUrl.Contains("mp4")) {
                            bestUrl = CleanExtractedUrl(rawUrl);
                            Logger.Info($"Rule34Video: Found candidate {key}: {bestUrl}");
                            break; 
                        }
                    }
                }

                if (bestUrl != null) {
                    Logger.Info($"Rule34Video: Initial extracted URL: {bestUrl}");
                    
                    // 3. Append rnd token if known and not already present
                    if (!string.IsNullOrEmpty(rndToken) && !bestUrl.Contains("rnd=")) {
                        var separator = bestUrl.Contains("?") ? "&" : "?";
                        bestUrl += $"{separator}rnd={rndToken}";
                        Logger.Info($"Rule34Video: Appended rnd token. Intermediate URL: {bestUrl}");
                    }
                    
                    // 4. Resolve redirect (Rule34Video uses get_file php script that redirects to CDN)
                    // We need to resolve this using the same session (cookies) as the page fetch
                    Logger.Info($"Rule34Video: Attempting to resolve redirect for: {bestUrl}");
                    var resolvedUrl = await _htmlFetcher.ResolveRedirectUrlAsync(bestUrl, url, cancellationToken);
                    Logger.Info($"Rule34Video: ResolveRedirectUrlAsync returned: {resolvedUrl ?? "null"}");
                    
                    if (!string.IsNullOrEmpty(resolvedUrl) && resolvedUrl != bestUrl) {
                        Logger.Info($"Rule34Video: Resolved redirect from {bestUrl} to {resolvedUrl}");
                        return resolvedUrl;
                    }

                    Logger.Info($"Rule34Video: No redirect occurred, returning intermediate URL: {bestUrl}");
                    return bestUrl;
                }

                Logger.Warning("Rule34Video: generic fallback triggered");
                return ExtractVideoFromHtml(html, url);
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

            Logger.Info($"ExtractVideoFromHtml: Processing {html.Length} characters of HTML from {baseUrl}");

            try {
                // Method 1: Look for <video> tags with src attribute
                Logger.Info("Trying Method 1: <video> tag with src attribute");
                var videoSrcPattern = @"<video[^>]+src\s*=\s*[""']([^""']+)[""']";
                var match = Regex.Match(html, videoSrcPattern, RegexOptions.IgnoreCase);
                if (match.Success && match.Groups.Count > 1) {
                    var videoUrl = match.Groups[1].Value;
                    var resolved = ResolveUrl(videoUrl, baseUrl);
                    Logger.Info($"Method 1: Found video URL: {resolved}");
                    return resolved;
                }

                // Method 2: Look for <source> tags within video elements
                Logger.Info("Trying Method 2: <source> tags");
                var sourcePattern = @"<source[^>]+src\s*=\s*[""']([^""']+)[""']";
                var sourceMatches = Regex.Matches(html, sourcePattern, RegexOptions.IgnoreCase);
                Logger.Info($"Method 2: Found {sourceMatches.Count} <source> tags");
                foreach (Match sourceMatch in sourceMatches) {
                    if (sourceMatch.Success && sourceMatch.Groups.Count > 1) {
                        var videoUrl = CleanExtractedUrl(sourceMatch.Groups[1].Value);
                        // Check for video extensions, allowing for query parameters
                        if (HasVideoExtension(videoUrl)) {
                            var resolved = ResolveUrl(videoUrl, baseUrl);
                            Logger.Info($"Method 2: Found video URL: {resolved}");
                            return resolved;
                        }
                    }
                }

                // Method 3: Look for data-url or data-src attributes
                Logger.Info("Trying Method 3: data attributes");
                var dataUrlPattern = @"(?:data-url|data-src|data-video-src|data-config)\s*=\s*[""']([^""']+)[""']";
                var dataMatches = Regex.Matches(html, dataUrlPattern, RegexOptions.IgnoreCase);
                foreach (Match dataMatch in dataMatches) {
                    if (dataMatch.Success && dataMatch.Groups.Count > 1) {
                        var url = CleanExtractedUrl(dataMatch.Groups[1].Value);
                        if (HasVideoExtension(url)) {
                            var resolved = ResolveUrl(url, baseUrl);
                            Logger.Info($"Method 3: Found video URL: {resolved}");
                            return resolved;
                        }
                    }
                }

                // Method 4: Look for video URLs in JavaScript variables
                Logger.Info("Trying Method 4: JavaScript variables");
                var jsVideoPatterns = new[] {
                    @"(?:src|url|source|videoUrl|file|video_url|video_alt_url|video_alt_url2|video_alt_url3)\s*[:=]\s*[""']([^""']*\.(?:mp4|webm|mkv|avi|mov|wmv|m4v|mpg|mpeg|ts|m2ts)[^""']*)[""']",
                    @"[""']([^""']*\.(?:mp4|webm|mkv|avi|mov|wmv|m4v|mpg|mpeg|ts|m2ts)[^""']*)[""']"
                };

                // Store potential matches to find the best quality if multiple exist
                var potentialUrls = new List<string>();

                foreach (var pattern in jsVideoPatterns) {
                    var jsMatches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);
                    Logger.Info($"Method 4: Pattern {pattern} found {jsMatches.Count} potential matches");
                    foreach (Match jsMatch in jsMatches) {
                        if (jsMatch.Success && jsMatch.Groups.Count > 1) {
                            var videoUrl = CleanExtractedUrl(jsMatch.Groups[1].Value);
                            var resolvedUrl = ResolveUrl(videoUrl, baseUrl);
                            if (IsValidVideoUrl(resolvedUrl)) {
                                potentialUrls.Add(resolvedUrl);
                            }
                        }
                    }
                }

                if (potentialUrls.Any()) {
                    // Try to find the highest quality (often denoted by _1080p, _720p, etc. in the URL)
                    // Or just pick the last one if it's from variables like video_alt_url3
                    var bestUrl = potentialUrls.OrderByDescending(u => {
                        if (u.Contains("_1080p") || u.Contains("1080")) return 1080;
                        if (u.Contains("_720p") || u.Contains("720")) return 720;
                        if (u.Contains("_480p") || u.Contains("480")) return 480;
                        if (u.Contains("_360p") || u.Contains("360")) return 360;
                        return 0;
                    }).First();

                    Logger.Info($"Method 4: Selected best video URL: {bestUrl}");
                    return bestUrl;
                }

                // Method 5: Look for HLS streams (m3u8)
                Logger.Info("Trying Method 5: HLS streams");
                var hlsPattern = @"[""']([^""']+\.m3u8(?:[^""']*)?)[""']";
                var hlsMatches = Regex.Matches(html, hlsPattern, RegexOptions.IgnoreCase);
                Logger.Info($"Method 5: Found {hlsMatches.Count} potential HLS streams");
                foreach (Match hlsMatch in hlsMatches) {
                    if (hlsMatch.Success && hlsMatch.Groups.Count > 1) {
                        var hlsUrl = CleanExtractedUrl(hlsMatch.Groups[1].Value);
                        var resolved = ResolveUrl(hlsUrl, baseUrl);
                        Logger.Info($"Method 5: Found HLS URL: {resolved}");
                        return resolved;
                    }
                }

                // Method 6: Look for Plyr source configuration
                Logger.Info("Trying Method 6: Plyr configuration");
                var plyrPattern = @"plyr_player\.source\s*=\s*\{[\s\S]*?sources\s*:\s*\[\s*\{\s*[""']?src[""']?\s*:\s*[""']([^""']+)[""']";
                var plyrMatch = Regex.Match(html, plyrPattern, RegexOptions.IgnoreCase);
                if (plyrMatch.Success && plyrMatch.Groups.Count > 1) {
                    var videoUrl = CleanExtractedUrl(plyrMatch.Groups[1].Value);
                    var resolved = ResolveUrl(videoUrl, baseUrl);
                    Logger.Info($"Method 6: Found Plyr URL: {resolved}");
                    return resolved;
                }

                // Method 7: Look for video URLs in JSON data
                Logger.Info("Trying Method 7: JSON data");
                var jsonVideoUrl = ExtractVideoFromJson(html, baseUrl);
                if (jsonVideoUrl != null) {
                    Logger.Info($"Method 7: Found JSON video URL: {jsonVideoUrl}");
                    return jsonVideoUrl;
                }

                Logger.Warning("ExtractVideoFromHtml: All extraction methods failed");
                return null;
            } catch (Exception ex) {
                Logger.Warning($"Error extracting video from HTML: {ex.Message}");
                return null;
            }
        }

        private bool HasVideoExtension(string url) {
            if (string.IsNullOrWhiteSpace(url)) return false;
            
            // Extract the path part before query parameters/fragments
            string path = url;
            int queryIndex = url.IndexOf('?');
            if (queryIndex != -1) path = url.Substring(0, queryIndex);
            
            int fragmentIndex = path.IndexOf('#');
            if (fragmentIndex != -1) path = path.Substring(0, fragmentIndex);
            
            // Remove trailing slash if present (common in some Hypnotube video links)
            path = path.TrimEnd('/');
            
            return Constants.VideoExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
        }

        private string ExtractVideoFromJson(string html, string baseUrl) {
            if (string.IsNullOrWhiteSpace(html)) return null;

            try {
                // Look for JSON-like structures that might contain video URLs
                // This version is more lenient with whitespace and escaped characters
                var jsonPattern = @"""(?:src|url|source|file|videoUrl)""\s*:\s*""([^""]+\.(?:mp4|webm|mkv|avi|mov|wmv|m4v|mpg|mpeg|ts|m2ts)[^""]*)""";
                var matches = Regex.Matches(html, jsonPattern, RegexOptions.IgnoreCase);
                
                foreach (Match match in matches) {
                    if (match.Success && match.Groups.Count > 1) {
                        var videoUrl = CleanExtractedUrl(match.Groups[1].Value);
                        
                        var resolvedUrl = ResolveUrl(videoUrl, baseUrl);
                        if (IsValidVideoUrl(resolvedUrl)) {
                            return resolvedUrl;
                        }
                    }
                }

                return null;
            } catch (Exception ex) {
                Logger.Warning($"Error extracting video from JSON: {ex.Message}");
                return null;
            }
        }

        private string CleanExtractedUrl(string url) {
            if (string.IsNullOrWhiteSpace(url)) return url;

            // Remove whitespace
            url = url.Trim();

            // Handle escaped slashes
            url = url.Replace("\\/", "/");

            // Some sites (like Rule34Video) prefix URLs with metadata like 'function/0/'
            // If the URL contains 'http' but not at the start, try to isolate the actual URL
            int httpIndex = url.IndexOf("http", StringComparison.OrdinalIgnoreCase);
            if (httpIndex > 0) {
                var potentialUrl = url.Substring(httpIndex);
                // Check if the isolated part is a valid absolute URL
                if (Uri.TryCreate(potentialUrl, UriKind.Absolute, out _)) {
                    Logger.Info($"CleanExtractedUrl: Isolated URL from prefix: {potentialUrl} (original: {url})");
                    return potentialUrl;
                }
            }

            return url;
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



