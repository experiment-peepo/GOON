using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TrainMeX.ViewModels;

namespace TrainMeX.Classes {
    /// <summary>
    /// Service for importing playlists from supported video sites
    /// </summary>
    public class PlaylistImporter {
        private readonly VideoUrlExtractor _urlExtractor;
        private static readonly HttpClient _httpClient;

        static PlaylistImporter() {
            _httpClient = new HttpClient {
                Timeout = TimeSpan.FromSeconds(Constants.HttpRequestTimeoutSeconds)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        public PlaylistImporter(VideoUrlExtractor urlExtractor) {
            _urlExtractor = urlExtractor ?? throw new ArgumentNullException(nameof(urlExtractor));
        }

        /// <summary>
        /// Imports a playlist from a supported site
        /// </summary>
        /// <param name="playlistUrl">The playlist page URL</param>
        /// <param name="progressCallback">Optional callback for progress updates (current, total)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>List of VideoItems from the playlist</returns>
        public async Task<List<VideoItem>> ImportPlaylistAsync(
            string playlistUrl, 
            Action<int, int> progressCallback = null,
            CancellationToken cancellationToken = default) {
            
            if (string.IsNullOrWhiteSpace(playlistUrl)) {
                throw new ArgumentException("Playlist URL cannot be empty", nameof(playlistUrl));
            }

            var videoItems = new List<VideoItem>();

            try {
                var uri = new Uri(playlistUrl);
                var host = uri.Host.ToLowerInvariant();

                List<string> videoPageUrls;

                if (host.Contains("hypnotube.com")) {
                    videoPageUrls = await ExtractHypnotubePlaylistAsync(playlistUrl, cancellationToken);
                } else if (host.Contains("iwara.tv")) {
                    videoPageUrls = await ExtractIwaraPlaylistAsync(playlistUrl, cancellationToken);
                } else if (host.Contains("rule34video.com")) {
                    videoPageUrls = await ExtractRule34VideoPlaylistAsync(playlistUrl, cancellationToken);
                } else if (host.Contains("pmvhaven.com")) {
                    videoPageUrls = await ExtractPmvHavenPlaylistAsync(playlistUrl, cancellationToken);
                } else {
                    // Generic extraction
                    videoPageUrls = await ExtractGenericPlaylistAsync(playlistUrl, cancellationToken);
                }

                if (videoPageUrls == null || videoPageUrls.Count == 0) {
                    Logger.Warning($"No videos found in playlist: {playlistUrl}");
                    return videoItems;
                }

                int total = videoPageUrls.Count;
                int current = 0;

                // Extract direct video URLs and titles for each page URL
                foreach (var pageUrl in videoPageUrls) {
                    cancellationToken.ThrowIfCancellationRequested();

                    current++;
                    progressCallback?.Invoke(current, total);

                    try {
                        // Validate page URL first
                        if (!FileValidator.ValidateVideoUrl(pageUrl, out string validationError)) {
                            Logger.Warning($"Skipped invalid page URL: {pageUrl} - {validationError}");
                            continue;
                        }

                        // Try to extract direct video URL
                        string directUrl = null;
                        try {
                            directUrl = await _urlExtractor.ExtractVideoUrlAsync(pageUrl, cancellationToken);
                            
                            // Validate extracted direct URL if we got one
                            if (directUrl != null && !FileValidator.ValidateVideoUrl(directUrl, out string directUrlError)) {
                                Logger.Warning($"Extracted direct URL failed validation: {directUrl} - {directUrlError}. Using page URL as fallback.");
                                directUrl = null;
                            }
                        } catch (Exception ex) {
                            Logger.Warning($"Error extracting direct URL from {pageUrl}: {ex.Message}. Using page URL as fallback.");
                        }
                        
                        // Use direct URL if available and valid, otherwise use page URL (preserve existing behavior)
                        var videoUrl = directUrl ?? pageUrl;
                        
                        // Create video item
                        var videoItem = new VideoItem(videoUrl);
                        
                        // Try to extract title (but never fail VideoItem creation if it fails)
                        try {
                            var title = await _urlExtractor.ExtractVideoTitleAsync(pageUrl, cancellationToken);
                            if (!string.IsNullOrWhiteSpace(title)) {
                                videoItem.Title = title;
                            }
                        } catch (Exception ex) {
                            Logger.Warning($"Error extracting title from {pageUrl}: {ex.Message}. VideoItem created without title.");
                            // Continue - VideoItem will use URL-based name extraction
                        }
                        
                        // Validate the video item
                        videoItem.Validate();
                        
                        if (videoItem.ValidationStatus == FileValidationStatus.Valid) {
                            videoItems.Add(videoItem);
                        } else {
                            Logger.Warning($"Skipped invalid video URL: {videoUrl} - {videoItem.ValidationError}");
                        }
                    } catch (Exception ex) {
                        Logger.Warning($"Error processing video URL {pageUrl}: {ex.Message}");
                        // Continue with next video - never fail entire playlist import
                    }
                }

                return videoItems;
            } catch (OperationCanceledException) {
                Logger.Info("Playlist import was cancelled");
                throw;
            } catch (Exception ex) {
                Logger.Error($"Error importing playlist from {playlistUrl}", ex);
                throw;
            }
        }

        private async Task<List<string>> ExtractHypnotubePlaylistAsync(string url, CancellationToken cancellationToken) {
            try {
                var html = await FetchHtmlAsync(url, cancellationToken);
                if (string.IsNullOrWhiteSpace(html)) return new List<string>();

                return ExtractHypnotubeLinksFromHtml(html, url);
            } catch (Exception ex) {
                Logger.Warning($"Error extracting Hypnotube playlist: {ex.Message}");
                return new List<string>();
            }
        }

        private async Task<List<string>> ExtractIwaraPlaylistAsync(string url, CancellationToken cancellationToken) {
            try {
                var html = await FetchHtmlAsync(url, cancellationToken);
                if (string.IsNullOrWhiteSpace(html)) return new List<string>();

                return ExtractVideoLinksFromHtml(html, url, "iwara.tv");
            } catch (Exception ex) {
                Logger.Warning($"Error extracting Iwara playlist: {ex.Message}");
                return new List<string>();
            }
        }

        private async Task<List<string>> ExtractRule34VideoPlaylistAsync(string url, CancellationToken cancellationToken) {
            var allVideoUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            try {
                // Start with first page
                var currentUrl = url;
                var visitedPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var pageUrlsToFetch = new Queue<string>();
                pageUrlsToFetch.Enqueue(currentUrl);
                
                // Safety limit: don't fetch more than 50 pages
                const int maxPages = 50;
                int pagesFetched = 0;
                
                while (pageUrlsToFetch.Count > 0 && pagesFetched < maxPages) {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    currentUrl = pageUrlsToFetch.Dequeue();
                    if (visitedPages.Contains(currentUrl)) continue;
                    visitedPages.Add(currentUrl);
                    pagesFetched++;
                    
                    var html = await FetchHtmlAsync(currentUrl, cancellationToken);
                    if (string.IsNullOrWhiteSpace(html)) continue;
                    
                    // Extract video URLs from current page
                    var pageVideoUrls = ExtractRule34VideoLinksFromHtml(html, currentUrl);
                    foreach (var videoUrl in pageVideoUrls) {
                        allVideoUrls.Add(videoUrl);
                    }
                    
                    // Extract pagination links
                    try {
                        var nextPageUrl = ExtractNextPageUrl(html, currentUrl, "rule34video.com");
                        if (!string.IsNullOrWhiteSpace(nextPageUrl) && !visitedPages.Contains(nextPageUrl)) {
                            pageUrlsToFetch.Enqueue(nextPageUrl);
                        }
                    } catch (Exception pagEx) {
                        Logger.Warning($"Error extracting next page URL: {pagEx.Message}. Stopping pagination.");
                        break; // Stop pagination if there's an error
                    }
                }
                
                if (pagesFetched >= maxPages) {
                    Logger.Warning($"RULE34Video playlist extraction stopped after {maxPages} pages (safety limit)");
                }
                
                return allVideoUrls.ToList();
            } catch (Exception ex) {
                Logger.Warning($"Error extracting RULE34Video playlist: {ex.Message}");
                return allVideoUrls.ToList();
            }
        }

        private async Task<List<string>> ExtractPmvHavenPlaylistAsync(string url, CancellationToken cancellationToken) {
            try {
                var html = await FetchHtmlAsync(url, cancellationToken);
                if (string.IsNullOrWhiteSpace(html)) return new List<string>();

                return ExtractPmvHavenLinksFromHtml(html, url);
            } catch (Exception ex) {
                Logger.Warning($"Error extracting PMVHaven playlist: {ex.Message}");
                return new List<string>();
            }
        }

        private async Task<List<string>> ExtractGenericPlaylistAsync(string url, CancellationToken cancellationToken) {
            try {
                var html = await FetchHtmlAsync(url, cancellationToken);
                if (string.IsNullOrWhiteSpace(html)) return new List<string>();

                return ExtractVideoLinksFromHtml(html, url, null);
            } catch (Exception ex) {
                Logger.Warning($"Error extracting generic playlist: {ex.Message}");
                return new List<string>();
            }
        }

        private async Task<string> FetchHtmlAsync(string url, CancellationToken cancellationToken) {
            try {
                var response = await _httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            } catch (Exception ex) {
                Logger.Warning($"Error fetching HTML from {url}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts video links from Hypnotube playlist HTML using site-specific patterns
        /// Excludes recommended/related videos and focuses on playlist items
        /// </summary>
        private List<string> ExtractHypnotubeLinksFromHtml(string html, string baseUrl) {
            var videoUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try {
                var excludedExtensions = new[] { 
                    ".css", ".jpg", ".jpeg", ".png", ".gif", ".js", ".json", ".xml", ".ico", 
                    ".svg", ".woff", ".woff2", ".ttf", ".eot", ".pdf", ".zip", ".rar", 
                    ".txt", ".md", ".webp", ".bmp", ".tiff"
                };
                // Note: .html and .htm are NOT excluded for Hypnotube as video pages use .html extension

                // Pattern: Look for <a> tags with href pointing to video pages
                // Hypnotube typically uses /videos/ or /video/ paths, often ending with .html
                var linkPattern = @"<a[^>]+href\s*=\s*[""']([^""']+)[""'][^>]*>";
                var linkMatches = Regex.Matches(html, linkPattern, RegexOptions.IgnoreCase);

                foreach (Match match in linkMatches) {
                    if (match.Success && match.Groups.Count > 1) {
                        var href = match.Groups[1].Value;
                        
                        // Pre-filter: exclude URLs with non-video extensions (but allow .html/.htm for Hypnotube)
                        // Use Uri to extract path before query string/fragment for proper extension checking
                        try {
                            var testUri = ResolveUrl(href, baseUrl);
                            if (testUri != null && Uri.TryCreate(testUri, UriKind.Absolute, out Uri uri)) {
                                var path = uri.AbsolutePath.ToLowerInvariant();
                                if (excludedExtensions.Any(ext => path.EndsWith(ext))) {
                                    continue;
                                }
                            } else if (excludedExtensions.Any(ext => href.ToLowerInvariant().EndsWith(ext))) {
                                continue;
                            }
                        } catch {
                            // Fallback to simple check if URI parsing fails
                            if (excludedExtensions.Any(ext => href.ToLowerInvariant().EndsWith(ext))) {
                                continue;
                            }
                        }
                        
                        var resolvedUrl = ResolveUrl(href, baseUrl);
                        
                        if (resolvedUrl != null && IsHypnotubeVideoPageUrl(resolvedUrl)) {
                            videoUrls.Add(resolvedUrl);
                        }
                    }
                }

                return videoUrls.ToList();
            } catch (Exception ex) {
                Logger.Warning($"Error extracting Hypnotube video links from HTML: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Extracts video links from RULE34Video playlist HTML using site-specific patterns
        /// </summary>
        private List<string> ExtractRule34VideoLinksFromHtml(string html, string baseUrl) {
            var videoUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try {
                var excludedExtensions = new[] { 
                    ".css", ".jpg", ".jpeg", ".png", ".gif", ".js", ".json", ".xml", ".ico", 
                    ".svg", ".woff", ".woff2", ".ttf", ".eot", ".pdf", ".zip", ".rar", 
                    ".txt", ".md", ".html", ".htm", ".webp", ".bmp", ".tiff"
                };

                // Pattern: Look for <a> tags with href pointing to video pages
                // RULE34Video typically uses /videos/ paths with numeric IDs
                var linkPattern = @"<a[^>]+href\s*=\s*[""']([^""']+)[""'][^>]*>";
                var linkMatches = Regex.Matches(html, linkPattern, RegexOptions.IgnoreCase);

                foreach (Match match in linkMatches) {
                    if (match.Success && match.Groups.Count > 1) {
                        var href = match.Groups[1].Value;
                        
                        // Pre-filter: exclude URLs with non-video extensions
                        // Use Uri to extract path before query string/fragment for proper extension checking
                        try {
                            var testUri = ResolveUrl(href, baseUrl);
                            if (testUri != null && Uri.TryCreate(testUri, UriKind.Absolute, out Uri uri)) {
                                var path = uri.AbsolutePath.ToLowerInvariant();
                                if (excludedExtensions.Any(ext => path.EndsWith(ext))) {
                                    continue;
                                }
                            } else if (excludedExtensions.Any(ext => href.ToLowerInvariant().EndsWith(ext))) {
                                continue;
                            }
                        } catch {
                            // Fallback to simple check if URI parsing fails
                            if (excludedExtensions.Any(ext => href.ToLowerInvariant().EndsWith(ext))) {
                                continue;
                            }
                        }
                        
                        var resolvedUrl = ResolveUrl(href, baseUrl);
                        
                        if (resolvedUrl != null && IsRule34VideoPageUrl(resolvedUrl)) {
                            videoUrls.Add(resolvedUrl);
                        }
                    }
                }

                return videoUrls.ToList();
            } catch (Exception ex) {
                Logger.Warning($"Error extracting RULE34Video links from HTML: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Extracts video links from PMVHaven playlist HTML using site-specific patterns
        /// Targets playlist items specifically and excludes the currently playing video
        /// </summary>
        private List<string> ExtractPmvHavenLinksFromHtml(string html, string baseUrl) {
            var videoUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try {
                var excludedExtensions = new[] { 
                    ".css", ".jpg", ".jpeg", ".png", ".gif", ".js", ".json", ".xml", ".ico", 
                    ".svg", ".woff", ".woff2", ".ttf", ".eot", ".pdf", ".zip", ".rar", 
                    ".txt", ".md", ".html", ".htm", ".webp", ".bmp", ".tiff"
                };

                // Pattern: Look for <a> tags with href pointing to video pages
                // PMVHaven typically uses /video/ paths (singular)
                var linkPattern = @"<a[^>]+href\s*=\s*[""']([^""']+)[""'][^>]*>";
                var linkMatches = Regex.Matches(html, linkPattern, RegexOptions.IgnoreCase);

                foreach (Match match in linkMatches) {
                    if (match.Success && match.Groups.Count > 1) {
                        var href = match.Groups[1].Value;
                        var fullMatch = match.Value;
                        
                        // Exclude currently playing/active video - check for active/current classes
                        if (Regex.IsMatch(fullMatch, @"\b(?:active|current|playing|bg-\[#272727\]|border-orange-500)\b", RegexOptions.IgnoreCase)) {
                            // This might be the currently playing video, but we want all videos
                            // So we don't exclude it here - we want all playlist videos
                        }
                        
                        // Pre-filter: exclude URLs with non-video extensions
                        // Use Uri to extract path before query string/fragment for proper extension checking
                        try {
                            var testUri = ResolveUrl(href, baseUrl);
                            if (testUri != null && Uri.TryCreate(testUri, UriKind.Absolute, out Uri uri)) {
                                var path = uri.AbsolutePath.ToLowerInvariant();
                                if (excludedExtensions.Any(ext => path.EndsWith(ext))) {
                                    continue;
                                }
                            } else if (excludedExtensions.Any(ext => href.ToLowerInvariant().EndsWith(ext))) {
                                continue;
                            }
                        } catch {
                            // Fallback to simple check if URI parsing fails
                            if (excludedExtensions.Any(ext => href.ToLowerInvariant().EndsWith(ext))) {
                                continue;
                            }
                        }
                        
                        var resolvedUrl = ResolveUrl(href, baseUrl);
                        
                        if (resolvedUrl != null && IsPmvHavenVideoPageUrl(resolvedUrl)) {
                            videoUrls.Add(resolvedUrl);
                        }
                    }
                }

                return videoUrls.ToList();
            } catch (Exception ex) {
                Logger.Warning($"Error extracting PMVHaven links from HTML: {ex.Message}");
                return new List<string>();
            }
        }

        private List<string> ExtractVideoLinksFromHtml(string html, string baseUrl, string domainFilter) {
            var videoUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try {
                var baseUri = new Uri(baseUrl);
                var baseHost = baseUri.Host.ToLowerInvariant();

                // Excluded file extensions (non-video files)
                var excludedExtensions = new[] { 
                    ".css", ".jpg", ".jpeg", ".png", ".gif", ".js", ".json", ".xml", ".ico", 
                    ".svg", ".woff", ".woff2", ".ttf", ".eot", ".pdf", ".zip", ".rar", 
                    ".txt", ".md", ".html", ".htm" 
                };

                // Pattern 1: Look for <a> tags with href pointing to video pages
                var linkPattern = @"<a[^>]+href\s*=\s*[""']([^""']+)[""'][^>]*>";
                var linkMatches = Regex.Matches(html, linkPattern, RegexOptions.IgnoreCase);

                foreach (Match match in linkMatches) {
                    if (match.Success && match.Groups.Count > 1) {
                        var href = match.Groups[1].Value;
                        
                        // Pre-filter: exclude URLs with non-video extensions
                        if (excludedExtensions.Any(ext => href.ToLowerInvariant().EndsWith(ext))) {
                            continue;
                        }
                        
                        var resolvedUrl = ResolveUrl(href, baseUrl);
                        
                        if (resolvedUrl != null && IsVideoPageUrl(resolvedUrl, domainFilter ?? baseHost)) {
                            videoUrls.Add(resolvedUrl);
                        }
                    }
                }

                // Pattern 2: Look for video URLs in data attributes or JSON
                var dataUrlPattern = @"(?:data-url|data-src|video-url|href)\s*[:=]\s*[""']([^""']+)[""']";
                var dataMatches = Regex.Matches(html, dataUrlPattern, RegexOptions.IgnoreCase);

                foreach (Match match in dataMatches) {
                    if (match.Success && match.Groups.Count > 1) {
                        var url = match.Groups[1].Value;
                        
                        // Pre-filter: exclude URLs with non-video extensions
                        if (excludedExtensions.Any(ext => url.ToLowerInvariant().EndsWith(ext))) {
                            continue;
                        }
                        
                        var resolvedUrl = ResolveUrl(url, baseUrl);
                        
                        if (resolvedUrl != null && IsVideoPageUrl(resolvedUrl, domainFilter ?? baseHost)) {
                            videoUrls.Add(resolvedUrl);
                        }
                    }
                }

                return videoUrls.ToList();
            } catch (Exception ex) {
                Logger.Warning($"Error extracting video links from HTML: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Validates if a URL is a Hypnotube video page URL using strict site-specific patterns
        /// </summary>
        private bool IsHypnotubeVideoPageUrl(string url) {
            if (string.IsNullOrWhiteSpace(url)) return false;

            try {
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri)) return false;

                var host = uri.Host.ToLowerInvariant();
                if (!host.Contains("hypnotube.com")) return false;

                var path = uri.AbsolutePath.ToLowerInvariant();
                
                // Exclude file extensions (but allow .html/.htm for Hypnotube video pages)
                var excludedExtensions = new[] { 
                    ".css", ".jpg", ".jpeg", ".png", ".gif", ".js", ".json", ".xml", ".ico", 
                    ".svg", ".woff", ".woff2", ".ttf", ".eot", ".pdf", ".zip", ".rar", 
                    ".txt", ".md", ".webp", ".bmp", ".tiff"
                };
                if (excludedExtensions.Any(ext => path.EndsWith(ext))) {
                    return false;
                }

                // Exclude asset and non-video paths
                var excludedPaths = new[] { 
                    "/static/", "/assets/", "/css/", "/js/", "/images/", "/img/", 
                    "/fonts/", "/font/", "/media/", "/uploads/", "/files/", "/download/",
                    "/login", "/register", "/search", "/user", "/settings", "/about", 
                    "/contact", "/terms", "/privacy", "/help", "/faq", "/api/",
                    "/tags/", "/tag/", "/categories/", "/category/", "/upload",
                    "/filter-content/"
                };
                if (excludedPaths.Any(excluded => path.Contains(excluded))) {
                    return false;
                }

                // Exclude direct video file URLs
                var videoExtensions = Constants.VideoExtensions;
                if (videoExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) {
                    return false;
                }

                // Hypnotube video pages: /videos/ID or /video/ID pattern, often ending with .html
                // Allow video indicators anywhere in path (not just at start)
                if (path.Contains("/videos/") || path.Contains("/video/")) {
                    var pathSegments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (pathSegments.Length >= 2) {
                        var lastSegment = pathSegments[pathSegments.Length - 1];
                        // Remove .html/.htm extension for segment validation
                        var segmentWithoutExt = lastSegment;
                        if (lastSegment.EndsWith(".html")) {
                            segmentWithoutExt = lastSegment.Substring(0, lastSegment.Length - 5);
                        } else if (lastSegment.EndsWith(".htm")) {
                            segmentWithoutExt = lastSegment.Substring(0, lastSegment.Length - 4);
                        }
                        // Must have a non-empty identifier after /videos/ or /video/
                        if (!string.IsNullOrWhiteSpace(segmentWithoutExt) && segmentWithoutExt.Length >= 1) {
                            // Exclude common non-video paths
                            var excludedLastSegments = new[] { "new", "popular", "trending", "latest", "random", "search", "categories", "day", "week", "month", "year", "a", "g", "s", "t" };
                            if (!excludedLastSegments.Contains(segmentWithoutExt.ToLowerInvariant())) {
                                return true;
                            }
                        }
                    }
                }

                // Fallback: check path depth and segment characteristics (stricter than before)
                // Only use fallback if path contains video indicators
                if (path.Contains("/video")) {
                    var pathSegmentsFallback = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (pathSegmentsFallback.Length >= 2) {
                        var lastSegmentFallback = pathSegmentsFallback[pathSegmentsFallback.Length - 1];
                        // Remove .html/.htm extension for validation
                        var segmentWithoutExtFallback = lastSegmentFallback;
                        if (lastSegmentFallback.EndsWith(".html")) {
                            segmentWithoutExtFallback = lastSegmentFallback.Substring(0, lastSegmentFallback.Length - 5);
                        } else if (lastSegmentFallback.EndsWith(".htm")) {
                            segmentWithoutExtFallback = lastSegmentFallback.Substring(0, lastSegmentFallback.Length - 4);
                        }
                        // Exclude single characters and very short segments
                        if (segmentWithoutExtFallback.Length >= 3) {
                            // Exclude common non-video words
                            var excludedWords = new[] { "day", "week", "month", "year", "a", "g", "s", "t" };
                            if (!excludedWords.Contains(segmentWithoutExtFallback.ToLowerInvariant())) {
                                // If last segment looks like an ID or has reasonable length, likely a video page
                                if (segmentWithoutExtFallback.All(char.IsDigit) || segmentWithoutExtFallback.Length >= 5) {
                                    return true;
                                }
                            }
                        }
                    }
                }

                return false;
            } catch {
                return false;
            }
        }

        /// <summary>
        /// Validates if a URL is a RULE34Video video page URL using strict site-specific patterns
        /// </summary>
        private bool IsRule34VideoPageUrl(string url) {
            if (string.IsNullOrWhiteSpace(url)) return false;

            try {
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri)) return false;

                var host = uri.Host.ToLowerInvariant();
                if (!host.Contains("rule34video.com")) return false;

                var path = uri.AbsolutePath.ToLowerInvariant();
                
                // Exclude file extensions
                var excludedExtensions = new[] { 
                    ".css", ".jpg", ".jpeg", ".png", ".gif", ".js", ".json", ".xml", ".ico", 
                    ".svg", ".woff", ".woff2", ".ttf", ".eot", ".pdf", ".zip", ".rar", 
                    ".txt", ".md", ".html", ".htm", ".webp", ".bmp", ".tiff"
                };
                if (excludedExtensions.Any(ext => path.EndsWith(ext))) {
                    return false;
                }

                // Exclude asset and non-video paths
                var excludedPaths = new[] { 
                    "/static/", "/assets/", "/css/", "/js/", "/images/", "/img/", 
                    "/fonts/", "/font/", "/media/", "/uploads/", "/files/", "/download/",
                    "/login", "/register", "/search", "/user", "/settings", "/about", 
                    "/contact", "/terms", "/privacy", "/help", "/faq", "/api/",
                    "/tags/", "/tag/", "/categories/", "/category/", "/upload",
                    "/filter-content/"
                };
                if (excludedPaths.Any(excluded => path.Contains(excluded))) {
                    return false;
                }

                // Exclude direct video file URLs
                var videoExtensions = Constants.VideoExtensions;
                if (videoExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) {
                    return false;
                }

                // RULE34Video video pages: /videos/ID pattern (typically numeric)
                // Allow video indicators anywhere in path (not just at start)
                if (path.Contains("/videos/")) {
                    var pathSegments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (pathSegments.Length >= 2) {
                        var lastSegment = pathSegments[pathSegments.Length - 1];
                        // Must have a non-empty identifier after /videos/
                        if (!string.IsNullOrWhiteSpace(lastSegment) && lastSegment.Length >= 3) {
                            // Exclude common non-video paths
                            var excludedLastSegments = new[] { "new", "popular", "trending", "latest", "random", "search", "categories", "tags", "day", "week", "month", "year", "a", "g", "s", "t" };
                            if (!excludedLastSegments.Contains(lastSegment.ToLowerInvariant())) {
                                return true;
                            }
                        }
                    }
                }

                // Fallback: check path depth and segment characteristics (stricter than before)
                // Only use fallback if path contains video indicators
                if (path.Contains("/video")) {
                    var pathSegmentsFallback = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (pathSegmentsFallback.Length >= 2) {
                        var lastSegmentFallback = pathSegmentsFallback[pathSegmentsFallback.Length - 1];
                        // Exclude single characters and very short segments
                        if (lastSegmentFallback.Length >= 3) {
                            // Exclude common non-video words
                            var excludedWords = new[] { "day", "week", "month", "year", "a", "g", "s", "t" };
                            if (!excludedWords.Contains(lastSegmentFallback.ToLowerInvariant())) {
                                // If last segment looks like an ID or has reasonable length, likely a video page
                                if (lastSegmentFallback.All(char.IsDigit) || lastSegmentFallback.Length >= 5) {
                                    return true;
                                }
                            }
                        }
                    }
                }

                return false;
            } catch {
                return false;
            }
        }

        /// <summary>
        /// Validates if a URL is a PMVHaven video page URL using strict site-specific patterns
        /// </summary>
        private bool IsPmvHavenVideoPageUrl(string url) {
            if (string.IsNullOrWhiteSpace(url)) return false;

            try {
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri)) return false;

                var host = uri.Host.ToLowerInvariant();
                if (!host.Contains("pmvhaven.com")) return false;

                var path = uri.AbsolutePath.ToLowerInvariant();
                
                // Exclude file extensions
                var excludedExtensions = new[] { 
                    ".css", ".jpg", ".jpeg", ".png", ".gif", ".js", ".json", ".xml", ".ico", 
                    ".svg", ".woff", ".woff2", ".ttf", ".eot", ".pdf", ".zip", ".rar", 
                    ".txt", ".md", ".html", ".htm", ".webp", ".bmp", ".tiff"
                };
                if (excludedExtensions.Any(ext => path.EndsWith(ext))) {
                    return false;
                }

                // Exclude asset and non-video paths
                var excludedPaths = new[] { 
                    "/static/", "/assets/", "/css/", "/js/", "/images/", "/img/", 
                    "/fonts/", "/font/", "/media/", "/uploads/", "/files/", "/download/",
                    "/login", "/register", "/search", "/user", "/settings", "/about", 
                    "/contact", "/terms", "/privacy", "/help", "/faq", "/api/",
                    "/tags/", "/tag/", "/categories/", "/category/", "/upload",
                    "/filter-content/"
                };
                if (excludedPaths.Any(excluded => path.Contains(excluded))) {
                    return false;
                }

                // Exclude direct video file URLs
                var videoExtensions = Constants.VideoExtensions;
                if (videoExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) {
                    return false;
                }

                // PMVHaven video pages: /video/ID pattern (singular, not plural)
                // Allow video indicators anywhere in path (not just at start)
                if (path.Contains("/video/") || path.Contains("/videos/")) {
                    var pathSegments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (pathSegments.Length >= 2) {
                        var lastSegment = pathSegments[pathSegments.Length - 1];
                        // Must have a non-empty identifier after /video/ or /videos/
                        if (!string.IsNullOrWhiteSpace(lastSegment) && lastSegment.Length >= 3) {
                            // Exclude common non-video paths
                            var excludedLastSegments = new[] { "new", "popular", "trending", "latest", "random", "search", "categories", "tags", "day", "week", "month", "year", "a", "g", "s", "t" };
                            if (!excludedLastSegments.Contains(lastSegment.ToLowerInvariant())) {
                                return true;
                            }
                        }
                    }
                }

                // Fallback: check path depth and segment characteristics (stricter than before)
                // Only use fallback if path contains video indicators
                if (path.Contains("/video")) {
                    var pathSegmentsFallback = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (pathSegmentsFallback.Length >= 2) {
                        var lastSegmentFallback = pathSegmentsFallback[pathSegmentsFallback.Length - 1];
                        // Exclude single characters and very short segments
                        if (lastSegmentFallback.Length >= 3) {
                            // Exclude common non-video words
                            var excludedWords = new[] { "day", "week", "month", "year", "a", "g", "s", "t" };
                            if (!excludedWords.Contains(lastSegmentFallback.ToLowerInvariant())) {
                                // If last segment looks like an ID or has reasonable length, likely a video page
                                if (lastSegmentFallback.All(char.IsDigit) || lastSegmentFallback.Length >= 5) {
                                    return true;
                                }
                            }
                        }
                    }
                }

                return false;
            } catch {
                return false;
            }
        }

        private bool IsVideoPageUrl(string url, string domain) {
            if (string.IsNullOrWhiteSpace(url)) return false;

            try {
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri)) return false;

                var host = uri.Host.ToLowerInvariant();
                if (!host.Contains(domain.ToLowerInvariant())) return false;

                var path = uri.AbsolutePath.ToLowerInvariant();
                
                // Exclude file extensions (non-video files)
                var excludedExtensions = new[] { 
                    ".css", ".jpg", ".jpeg", ".png", ".gif", ".js", ".json", ".xml", ".ico", 
                    ".svg", ".woff", ".woff2", ".ttf", ".eot", ".pdf", ".zip", ".rar", 
                    ".txt", ".md", ".html", ".htm", ".webp", ".bmp", ".tiff"
                };
                if (excludedExtensions.Any(ext => path.EndsWith(ext))) {
                    return false;
                }

                // Exclude common asset paths
                var excludedAssetPaths = new[] { 
                    "/static/", "/assets/", "/css/", "/js/", "/images/", "/img/", 
                    "/fonts/", "/font/", "/media/", "/uploads/", "/files/", "/download/"
                };
                if (excludedAssetPaths.Any(excluded => path.Contains(excluded))) {
                    return false;
                }

                // Exclude common non-video pages
                var excludedPaths = new[] { 
                    "/login", "/register", "/search", "/user", "/settings", "/about", 
                    "/contact", "/terms", "/privacy", "/help", "/faq", "/api/"
                };
                if (excludedPaths.Any(excluded => path.Contains(excluded))) {
                    return false;
                }

                // Exclude direct video file URLs (those should be handled separately)
                var videoExtensions = Constants.VideoExtensions;
                if (videoExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) {
                    return false;
                }

                // Check if it looks like a video page (has /video/ or similar in path)
                var videoIndicators = new[] { "/video/", "/videos/", "/watch", "/view", "/play" };
                if (videoIndicators.Any(indicator => path.Contains(indicator))) {
                    return true;
                }

                // Require minimum path depth (at least 3 segments) to avoid root/home pages
                var pathSegments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (pathSegments.Length >= 2) {
                    // Additional check: ensure it's not just a category/tag page
                    // Most video pages have numeric IDs or slugs
                    var lastSegment = pathSegments[pathSegments.Length - 1];
                    // If last segment looks like an ID or has reasonable length, likely a video page
                    if (lastSegment.Length > 3 && (lastSegment.All(char.IsDigit) || lastSegment.Length >= 5)) {
                        return true;
                    }
                }

                return false;
            } catch {
                return false;
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

        /// <summary>
        /// Tries to extract HTML content from playlist-specific containers
        /// Returns the container HTML if found, otherwise returns the full HTML
        /// </summary>
        private string ExtractPlaylistContainerHtml(string html, string[] containerPatterns) {
            if (string.IsNullOrWhiteSpace(html)) return html;
            
            foreach (var pattern in containerPatterns) {
                try {
                    // Try to match container opening tag
                    var containerPattern = $@"<(?:\w+)[^>]*{pattern}[^>]*>.*?</(?:\w+)>";
                    var match = Regex.Match(html, containerPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (match.Success) {
                        return match.Value;
                    }
                } catch {
                    // Continue to next pattern
                }
            }
            
            return html; // Fallback to full HTML
        }

        /// <summary>
        /// Extracts the next page URL from HTML for pagination support
        /// Returns null if no next page found or if we should stop pagination
        /// </summary>
        private string ExtractNextPageUrl(string html, string currentUrl, string domain) {
            if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(currentUrl)) return null;
            
            try {
                if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out Uri currentUri)) return null;
                
                var basePath = currentUri.AbsolutePath;
                var query = currentUri.Query;
                
                // Strategy 1: Look for "next" link in pagination
                var nextLinkPatterns = new[] {
                    @"<a[^>]*href\s*=\s*[""']([^""']+)[""'][^>]*>(?:.*?<[^>]*>)*\s*(?:next|>|»)\s*<",
                    @"rel\s*=\s*[""']next[""'][^>]*href\s*=\s*[""']([^""']+)[""']",
                    @"href\s*=\s*[""']([^""']+)[""'][^>]*rel\s*=\s*[""']next[""']",
                };
                
                foreach (var pattern in nextLinkPatterns) {
                    var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (match.Success && match.Groups.Count > 1) {
                        var nextUrl = match.Groups[1].Value;
                        var resolved = ResolveUrl(nextUrl, currentUrl);
                        if (resolved != null && Uri.TryCreate(resolved, UriKind.Absolute, out Uri nextUri)) {
                            if (nextUri.Host.ToLowerInvariant().Contains(domain.ToLowerInvariant()) && 
                                resolved != currentUrl) {
                                return resolved;
                            }
                        }
                    }
                }
                
                // Strategy 2: Extract all page links and find the next one
                var pageLinkPattern = @"href\s*=\s*[""']([^""']*[?&]page[=_](\d+)[^""']*)[""']";
                var pageMatches = Regex.Matches(html, pageLinkPattern, RegexOptions.IgnoreCase);
                var pageNumbers = new HashSet<int>();
                
                foreach (Match match in pageMatches) {
                    if (match.Success && match.Groups.Count >= 3) {
                        var pageUrl = match.Groups[1].Value;
                        if (int.TryParse(match.Groups[2].Value, out int pageNum)) {
                            pageNumbers.Add(pageNum);
                            var resolved = ResolveUrl(pageUrl, currentUrl);
                            if (resolved != null && Uri.TryCreate(resolved, UriKind.Absolute, out Uri pageUri)) {
                                if (pageUri.Host.ToLowerInvariant().Contains(domain.ToLowerInvariant())) {
                                    // Store URLs for potential use
                                }
                            }
                        }
                    }
                }
                
                // Strategy 3: Check current URL for page parameter and increment
                var currentPageMatch = Regex.Match(query, @"[?&]page[=_](\d+)", RegexOptions.IgnoreCase);
                int currentPage = 1;
                if (currentPageMatch.Success && currentPageMatch.Groups.Count > 1) {
                    int.TryParse(currentPageMatch.Groups[1].Value, out currentPage);
                }
                
                // If we found page numbers in links, check if next page exists
                if (pageNumbers.Count > 0) {
                    var maxPage = pageNumbers.Max();
                    if (currentPage < maxPage) {
                        var nextPage = currentPage + 1;
                        // Construct next page URL
                        string nextQuery;
                        if (currentPageMatch.Success) {
                            // Replace existing page parameter
                            var pageParam = currentPageMatch.Groups[0].Value; // e.g., "?page=1" or "&page=1"
                            var isQueryStart = pageParam.StartsWith("?");
                            nextQuery = Regex.Replace(query, @"[?&]page[=_](\d+)", $"{(!isQueryStart ? "&" : "")}page={nextPage}", RegexOptions.IgnoreCase);
                            if (nextQuery.StartsWith("&")) nextQuery = "?" + nextQuery.Substring(1);
                        } else {
                            var separator = string.IsNullOrWhiteSpace(query) ? "?" : "&";
                            nextQuery = query + separator + $"page={nextPage}";
                        }
                        return $"{currentUri.Scheme}://{currentUri.Host}{basePath}{nextQuery}";
                    }
                } else if (currentPage == 1 && pageNumbers.Count == 0) {
                    // Only try page 2 if we're on page 1, no pagination found, AND we haven't already tried page 2
                    // This prevents infinite loops
                    var separator = string.IsNullOrWhiteSpace(query) ? "?" : "&";
                    var testPage2Url = $"{currentUri.Scheme}://{currentUri.Host}{basePath}{query}{separator}page=2";
                    // Only return if we haven't visited this URL yet (check is done by caller)
                    return testPage2Url;
                }
                
                return null; // No next page found
            } catch (Exception ex) {
                Logger.Warning($"Error extracting next page URL: {ex.Message}");
                return null;
            }
        }
    }
}



