using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using HtmlAgilityPack;
using GOON.ViewModels;

namespace GOON.Classes {
    /// <summary>
    /// Service for importing playlists from supported video sites
    /// </summary>
    public class PlaylistImporter {
        private readonly IVideoUrlExtractor _urlExtractor;
        private readonly IHtmlFetcher _htmlFetcher;

        public PlaylistImporter(IVideoUrlExtractor urlExtractor, IHtmlFetcher htmlFetcher = null) {
            _urlExtractor = urlExtractor ?? throw new ArgumentNullException(nameof(urlExtractor));
            _htmlFetcher = htmlFetcher ?? new StandardHtmlFetcher();
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

            cancellationToken.ThrowIfCancellationRequested();

            var videoItems = new List<VideoItem>();

            try {
                var uri = new Uri(playlistUrl);
                var host = uri.Host.ToLowerInvariant();

                List<string> videoPageUrls;

                if (host.Contains("hypnotube.com")) {
                    videoPageUrls = await ExtractHypnotubePlaylistAsync(playlistUrl, cancellationToken);
                } else if (host.Contains("rule34video.com")) {
                    videoPageUrls = await ExtractRule34VideoPlaylistAsync(playlistUrl, cancellationToken);
                } else if (host.Contains("pmvhaven.com")) {
                    videoPageUrls = await ExtractPmvHavenPlaylistAsync(playlistUrl, cancellationToken);
                } else if (host.Contains("redgifs.com")) {
                    Logger.Info($"[PlaylistImporter] Detected RedGifs URL: {playlistUrl}");
                    videoPageUrls = await ExtractRedgifsPlaylistAsync(playlistUrl, cancellationToken);
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

                        // Extract Metadata (Integrated URL and Title in one pass)
                        var metadata = await _urlExtractor.ExtractVideoMetadataAsync(pageUrl, cancellationToken);
                        var videoUrl = metadata.Url ?? pageUrl;
                        var title = metadata.Title;

                        if (string.IsNullOrWhiteSpace(title)) {
                            title = pageUrl.Split('/').Last();
                        }
                        
                        // Create video item
                        var videoItem = new VideoItem(videoUrl) {
                            Title = title,
                            OriginalPageUrl = pageUrl  // Store for re-extraction when URLs expire
                        };
                        
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
            var allVideoUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            try {
                // Start with first page
                var currentUrl = url;
                var visitedPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var pageUrlsToFetch = new Queue<string>();
                pageUrlsToFetch.Enqueue(currentUrl);
                
                var maxPages = App.Settings?.MaxPlaylistPages ?? 100;
                int pagesFetched = 0;

                // If this is a single video page, don't treat it as a playlist
                if (IsHypnotubeVideoPageUrl(url)) {
                    Logger.Info($"[Hypnotube] Recognized input as single video page, skipping playlist extraction: {url}");
                    return new List<string> { url };
                }
                
                while (pageUrlsToFetch.Count > 0 && pagesFetched < maxPages) {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    currentUrl = pageUrlsToFetch.Dequeue();
                    if (visitedPages.Contains(currentUrl)) continue;
                    visitedPages.Add(currentUrl);
                    pagesFetched++;
                    
                    var html = await FetchHtmlAsync(currentUrl, cancellationToken);
                    if (string.IsNullOrWhiteSpace(html)) continue;
                    
                    // Extract video URLs from current page
                    var pageVideoUrls = ExtractHypnotubeLinksFromHtml(html, currentUrl);
                    foreach (var videoUrl in pageVideoUrls) {
                        allVideoUrls.Add(videoUrl);
                    }
                    
                    // Extract pagination links
                    try {
                        var nextPageUrl = ExtractNextPageUrl(html, currentUrl, "hypnotube.com");
                        if (!string.IsNullOrWhiteSpace(nextPageUrl) && !visitedPages.Contains(nextPageUrl)) {
                            pageUrlsToFetch.Enqueue(nextPageUrl);
                        }
                    } catch (Exception pagEx) {
                        Logger.Warning($"Error extracting Hypnotube next page URL: {pagEx.Message}");
                        break;
                    }
                }
                
                return allVideoUrls.ToList();
            } catch (Exception ex) {
                Logger.Warning($"Error extracting Hypnotube playlist: {ex.Message}");
                return allVideoUrls.ToList();
            }
        }



        private async Task<List<string>> ExtractRedgifsPlaylistAsync(string url, CancellationToken cancellationToken) {
            var videoUrls = new List<string>();
            try {
                Logger.Info($"[RedGifs] Starting playlist extraction for {url}");

                // Match the last part of the URL (either display name or ID)
                var uri = new Uri(url);
                var usernameMatch = Regex.Match(uri.AbsolutePath, @"/users/([^/?&]+)", RegexOptions.IgnoreCase);
                if (!usernameMatch.Success) {
                    Logger.Warning("[RedGifs] Could not extract username from URL - pattern mismatch");
                    return videoUrls;
                }
                var username = usernameMatch.Groups[1].Value;
                Logger.Info($"[RedGifs] Extracted username from URL: {username}");

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                // 1. Get temp token
                var authResponse = await httpClient.GetStringAsync("https://api.redgifs.com/v2/auth/temporary");
                var tokenMatch = Regex.Match(authResponse, @"""token""\s*:\s*""([^""]+)""");
                if (!tokenMatch.Success) {
                    // Try alternative pattern if direct match fails
                    tokenMatch = Regex.Match(authResponse, @"access_token""\s*:\s*""([^""]+)""");
                }

                if (!tokenMatch.Success) {
                    Logger.Warning($"[RedGifs] Failed to extract auth token from: {authResponse.Substring(0, Math.Min(authResponse.Length, 100))}");
                    return videoUrls;
                }
                var token = tokenMatch.Groups[1].Value;

                // 2. Fetch User GIFs
                // We'll try the username first, then if it fails or returns 0, we could potentially try to find the real ID from the page
                var apiUrl = $"https://api.redgifs.com/v2/users/{username}/search?order=new&count=100";
                var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                request.Headers.Add("Authorization", $"Bearer {token}");

                var response = await httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode) {
                    Logger.Warning($"[RedGifs] API failed for {username} with status: {response.StatusCode}");
                    return videoUrls;
                }
                
                var json = await response.Content.ReadAsStringAsync();
                
                // 3. Extract IDs - Safely parse JSON to isolate the 'gifs' array
                var uniqueIds = new HashSet<string>();
                try {
                    using (JsonDocument doc = JsonDocument.Parse(json)) {
                        if (doc.RootElement.TryGetProperty("gifs", out JsonElement gifsElement) && 
                            gifsElement.ValueKind == JsonValueKind.Array) {
                            
                            foreach (JsonElement gif in gifsElement.EnumerateArray()) {
                                if (gif.TryGetProperty("id", out JsonElement idElement)) {
                                    string id = idElement.GetString();
                                    if (!string.IsNullOrEmpty(id) && !uniqueIds.Contains(id)) {
                                        uniqueIds.Add(id);
                                        videoUrls.Add($"https://www.redgifs.com/watch/{id}");
                                    }
                                }
                            }
                        }
                    }
                } catch (Exception jsonEx) {
                    Logger.Warning($"[RedGifs] JSON parsing failed: {jsonEx.Message}. Falling back to regex...");
                    // Emergency regex fallback if JSON is malformed
                    var idMatches = Regex.Matches(json, @"""id""\s*:\s*""([^""]+)""");
                    foreach (Match match in idMatches) {
                        if (match.Success && match.Groups.Count > 1) {
                            var id = match.Groups[1].Value;
                            if (!string.IsNullOrEmpty(id) && !uniqueIds.Contains(id)) {
                                uniqueIds.Add(id);
                                videoUrls.Add($"https://www.redgifs.com/watch/{id}");
                            }
                        }
                    }
                }

                if (videoUrls.Count == 0) {
                    Logger.Info($"[RedGifs] No videos found for {username} via API. Attempting HTML fallback...");
                    // Fallback to HTML extraction if API returns nothing (e.g. if username differs from profile URL)
                    var html = await response.Content.ReadAsStringAsync(); // Reuse response or fetch fresh?
                    // Fetch fresh HTML from the user profile page directly
                    var profileHtml = await httpClient.GetStringAsync(url);
                    // Extract IDs from thumbnail sources (as found by browser subagent)
                    var thumbPattern = @"media\.redgifs\.com/([^""'-]+)-mobile\.jpg";
                    var thumbMatches = Regex.Matches(profileHtml, thumbPattern, RegexOptions.IgnoreCase);
                    foreach (Match m in thumbMatches) {
                        var id = m.Groups[1].Value;
                        if (!uniqueIds.Contains(id)) {
                            uniqueIds.Add(id);
                            videoUrls.Add($"https://www.redgifs.com/watch/{id}");
                        }
                    }
                }

                Logger.Info($"[RedGifs] Total extracted {videoUrls.Count} videos for user {username}");
                return videoUrls;

            } catch (Exception ex) {
                Logger.Warning($"Error extracting RedGifs playlist: {ex.Message}");
                return videoUrls;
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
                
                // Use configurable max pages from user settings
                var maxPages = App.Settings?.MaxPlaylistPages ?? 100;
                int pagesFetched = 0;

                // If this is a single video page, don't treat it as a playlist
                if (IsRule34VideoPageUrl(url)) {
                    Logger.Info($"[Rule34Video] Recognized input as single video page, skipping playlist extraction: {url}");
                    return new List<string> { url };
                }
                
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
                // If this is a single video page, don't treat it as a playlist
                if (IsPmvHavenVideoPageUrl(url)) {
                    Logger.Info($"[PMVHaven] Recognized input as single video page, skipping playlist extraction: {url}");
                    return new List<string> { url };
                }

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
            return await _htmlFetcher.FetchHtmlAsync(url, cancellationToken);
        }

        /// <summary>
        /// Extracts video links from Hypnotube playlist HTML using regex patterns
        /// Hypnotube video pages have pattern: /video/[slug]-[id].html with optional ?p=[playlist_id]
        /// </summary>
        private List<string> ExtractHypnotubeLinksFromHtml(string html, string baseUrl) {
            var videoUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try {
                // Extract playlist ID from URL if present (for filtering related videos)
                var playlistIdMatch = Regex.Match(baseUrl, @"/playlist/(\d+)", RegexOptions.IgnoreCase);
                var playlistId = playlistIdMatch.Success ? playlistIdMatch.Groups[1].Value : null;

                // Pattern to find all <a> tags with href containing /video/ path
                var linkPattern = @"<a[^>]*href\s*=\s*[""']([^""']*\/video\/[^""']+\.html[^""']*)[""'][^>]*>";
                var matches = Regex.Matches(html, linkPattern, RegexOptions.IgnoreCase);

                Logger.Info($"[Hypnotube] Found {matches.Count} potential video links in HTML");

                foreach (Match match in matches) {
                    if (match.Success && match.Groups.Count > 1) {
                        var href = match.Groups[1].Value;
                        var resolvedUrl = ResolveUrl(href, baseUrl);
                        
                        if (resolvedUrl != null && IsHypnotubeVideoPageUrl(resolvedUrl)) {
                            // If we're on a playlist page and link has the same playlist parameter, prioritize it
                            // But also include links without the parameter (they might be the same videos)
                            if (playlistId != null) {
                                // Both links with p= parameter and without are valid playlist items
                                if (href.Contains($"?p={playlistId}") || href.Contains($"&p={playlistId}")) {
                                    videoUrls.Add(resolvedUrl);
                                } else if (!href.Contains("?p=") && !href.Contains("&p=")) {
                                    // Links without any playlist parameter - add them too
                                    videoUrls.Add(resolvedUrl);
                                }
                            } else {
                                // Not a playlist page, add all video links
                                videoUrls.Add(resolvedUrl);
                            }
                        }
                    }
                }

                // Fallback: Also try HtmlAgilityPack for better parsing
                if (videoUrls.Count == 0) {
                    Logger.Info("[Hypnotube] Regex found no videos, trying HtmlAgilityPack fallback");
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);
                    
                    // Select all links that point to video pages
                    var allLinks = doc.DocumentNode.SelectNodes("//a[@href]");
                    if (allLinks != null) {
                        foreach (var link in allLinks) {
                            var href = link.GetAttributeValue("href", "");
                            if (href.Contains("/video/") && href.Contains(".html")) {
                                var resolvedUrl = ResolveUrl(href, baseUrl);
                                if (resolvedUrl != null && IsHypnotubeVideoPageUrl(resolvedUrl)) {
                                    videoUrls.Add(resolvedUrl);
                                }
                            }
                        }
                    }
                }

                Logger.Info($"[Hypnotube] Extracted {videoUrls.Count} unique video URLs");
                return videoUrls.ToList();
            } catch (Exception ex) {
                Logger.Warning($"Error extracting Hypnotube video links from HTML: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Extracts video links from RULE34Video playlist HTML using site-specific patterns
        /// Rule34Video uses /videos/ID/slug format for video pages
        /// </summary>
        private List<string> ExtractRule34VideoLinksFromHtml(string html, string baseUrl) {
            var videoUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try {
                Logger.Info("[Rule34Video] Starting link extraction with HtmlAgilityPack");
                
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Find the h2 header that contains 'Videos' to identify the uploads section
                // Then find the first .thumbs container following it.
                var headerNode = doc.DocumentNode.SelectSingleNode("//h2[contains(., 'Videos')]");
                HtmlNode mainThumbs = null;

                if (headerNode != null) {
                    mainThumbs = headerNode.SelectSingleNode("./following::div[contains(@class, 'thumbs')][1]");
                    if (mainThumbs != null) {
                        Logger.Info("[Rule34Video] Successfully isolated the 'Videos' container.");
                    }
                }

                // Fallback to the very first .thumbs if header-based selection failed
                if (mainThumbs == null) {
                    var contentArea = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'content_general')]") ?? doc.DocumentNode;
                    mainThumbs = contentArea.SelectSingleNode(".//div[contains(@class, 'thumbs')]");
                }

                if (mainThumbs != null) {
                    var links = mainThumbs.SelectNodes(".//a[contains(@href, '/video/')]");
                    if (links != null) {
                        Logger.Info($"[Rule34Video] Found {links.Count} videos in targeted container.");
                        foreach (var link in links) {
                            var href = link.GetAttributeValue("href", "");
                            var resolvedUrl = ResolveUrl(href, baseUrl);
                            if (resolvedUrl != null && IsRule34VideoPageUrl(resolvedUrl)) {
                                videoUrls.Add(resolvedUrl);
                            }
                        }
                    }
                }

                // If nothing found, one last attempt at regex but with a stricter pattern to avoid favorites
                if (videoUrls.Count == 0 && !html.Contains("'s Favorites")) {
                    Logger.Info("[Rule34Video] No containers found, using strict regex fallback (no favorites on page)");
                    var videoPattern = @"href\s*=\s*[""']([^""']*/videos?/\d+[^""']*)[""']";
                    var videoMatches = Regex.Matches(html, videoPattern, RegexOptions.IgnoreCase);
                    
                    foreach (Match match in videoMatches) {
                        if (match.Success && match.Groups.Count > 1) {
                            var href = match.Groups[1].Value;
                            var resolvedUrl = ResolveUrl(href, baseUrl);
                            if (resolvedUrl != null && IsRule34VideoPageUrl(resolvedUrl)) {
                                videoUrls.Add(resolvedUrl);
                            }
                        }
                    }
                }
                
                Logger.Info($"[Rule34Video] Extracted {videoUrls.Count} unique video URLs");
                return videoUrls.ToList();
            } catch (Exception ex) {
                Logger.Warning($"Error extracting RULE34Video links from HTML: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Extracts video links from PMVHaven playlist HTML using LD+JSON metadata
        /// PMVHaven is a SPA that uses JavaScript buttons instead of traditional links
        /// The LD+JSON ItemList contains all video URLs reliably
        /// </summary>
        private List<string> ExtractPmvHavenLinksFromHtml(string html, string baseUrl) {
            var videoUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try {
                Logger.Info("[PMVHaven] Starting playlist extraction");
                
                // PMVHaven uses LD+JSON with @type: ItemList for playlists
                // Extract JSON from <script type="application/ld+json">
                var ldJsonPattern = @"<script\s+type=[""']application/ld\+json[""'][^>]*>(.*?)</script>";
                var ldJsonMatches = Regex.Matches(html, ldJsonPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

                Logger.Info($"[PMVHaven] Found {ldJsonMatches.Count} LD+JSON blocks");

                foreach (Match match in ldJsonMatches) {
                    if (match.Success && match.Groups.Count > 1) {
                        var jsonContent = match.Groups[1].Value.Trim();
                        
                        try {
                            // Check if this is an ItemList (playlist)
                            if (jsonContent.Contains("\"@type\"") && jsonContent.Contains("\"ItemList\"")) {
                                Logger.Info("[PMVHaven] Found LD+JSON ItemList");
                                
                                // Try multiple URL patterns that PMVHaven might use
                                var urlPatterns = new[] {
                                    @"""embedUrl""\s*:\s*""([^""]+)""",      // embedUrl field
                                    @"""url""\s*:\s*""([^""]+/video[^""]+)""", // url field containing /video
                                    @"""contentUrl""\s*:\s*""([^""]+)""",   // contentUrl field
                                    @"""@id""\s*:\s*""([^""]+/video[^""]+)""" // @id field containing /video
                                };
                                
                                foreach (var pattern in urlPatterns) {
                                    var urlMatches = Regex.Matches(jsonContent, pattern, RegexOptions.IgnoreCase);
                                    
                                    foreach (Match urlMatch in urlMatches) {
                                        if (urlMatch.Success && urlMatch.Groups.Count > 1) {
                                            var extractedUrl = urlMatch.Groups[1].Value;
                                            
                                            // Clean up escaped URLs
                                            extractedUrl = extractedUrl.Replace("\\/", "/");
                                            
                                            // Filter: Only accept pmvhaven.com VIDEO PAGE URLs, not CDN/direct video URLs
                                            // Skip: video.pmvhaven.com (CDN), .mp4, .m3u8, .webm files
                                            if (!string.IsNullOrEmpty(extractedUrl) && 
                                                extractedUrl.Contains("pmvhaven.com/videos/") &&
                                                !extractedUrl.Contains("video.pmvhaven.com") &&
                                                !extractedUrl.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) &&
                                                !extractedUrl.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase) &&
                                                !extractedUrl.EndsWith(".webm", StringComparison.OrdinalIgnoreCase) &&
                                                !extractedUrl.Contains("/master.m3u8")) {
                                                videoUrls.Add(extractedUrl);
                                                Logger.Info($"[PMVHaven] Extracted page URL: {extractedUrl}");
                                            }
                                        }
                                    }
                                }
                                
                                if (videoUrls.Count > 0) {
                                    Logger.Info($"[PMVHaven] Successfully extracted {videoUrls.Count} videos from LD+JSON");
                                    return videoUrls.ToList();
                                }
                            }
                        } catch (Exception jsonEx) {
                            Logger.Warning($"[PMVHaven] Error parsing LD+JSON: {jsonEx.Message}");
                        }
                    }
                }

                // Fallback 1: Look for video links in href attributes
                Logger.Info("[PMVHaven] LD+JSON extraction yielded no results, trying href fallback");
                var hrefPattern = @"href\s*=\s*[""']([^""']*(?:/video|/videos)/[^""']+)[""']";
                var hrefMatches = Regex.Matches(html, hrefPattern, RegexOptions.IgnoreCase);
                
                foreach (Match hrefMatch in hrefMatches) {
                    if (hrefMatch.Success && hrefMatch.Groups.Count > 1) {
                        var href = hrefMatch.Groups[1].Value;
                        var resolvedUrl = ResolveUrl(href, baseUrl);
                        if (resolvedUrl != null && IsPmvHavenVideoPageUrl(resolvedUrl)) {
                            videoUrls.Add(resolvedUrl);
                        }
                    }
                }
                
                if (videoUrls.Count > 0) {
                    Logger.Info($"[PMVHaven] Extracted {videoUrls.Count} videos from href fallback");
                    return videoUrls.ToList();
                }
                
                // Fallback 2: More aggressive regex for any pmvhaven video URL in the page
                Logger.Info("[PMVHaven] Trying aggressive URL extraction");
                var aggressivePattern = @"https?://(?:www\.)?pmvhaven\.com/(?:video|videos)/[^\s\""'<>]+";
                var aggressiveMatches = Regex.Matches(html, aggressivePattern, RegexOptions.IgnoreCase);
                
                foreach (Match aggressiveMatch in aggressiveMatches) {
                    if (aggressiveMatch.Success) {
                        var url = aggressiveMatch.Value;
                        // Clean trailing punctuation
                        url = Regex.Replace(url, @"[.,;:!?)]+$", "");
                        if (IsPmvHavenVideoPageUrl(url)) {
                            videoUrls.Add(url);
                        }
                    }
                }

                Logger.Info($"[PMVHaven] Total extracted: {videoUrls.Count} videos");
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
                    ".txt", ".md" 
                };

                // Pattern 1: Look for <a> tags with href pointing to video pages
                var linkPattern = @"<a[^>]*href\s*=\s*[""']([^""']+)[""']";
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
                
                // Remove trailing slash for extension check
                var pathForExtension = path.TrimEnd('/');
                
                // Exclude file extensions (but allow .html/.htm for Hypnotube video pages)
                var excludedExtensions = new[] { 
                    ".css", ".jpg", ".jpeg", ".png", ".gif", ".js", ".json", ".xml", ".ico", 
                    ".svg", ".woff", ".woff2", ".ttf", ".eot", ".pdf", ".zip", ".rar", 
                    ".txt", ".md", ".webp", ".bmp", ".tiff"
                };
                if (excludedExtensions.Any(ext => pathForExtension.EndsWith(ext))) {
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

                // Direct video file URLs are allowed (will be returned as-is by Extractor)
                if (Constants.VideoExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) {
                    return true;
                }

                // Hypnotube video pages: /videos/ID or /video/ID pattern, often ending with .html
                // Allow video indicators anywhere in path (not just at start)
                if (path.Contains("/videos") || path.Contains("/video")) {
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

                // Direct video file URLs are allowed
                if (Constants.VideoExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) {
                    return true;
                }

                // PMVHaven video pages: /video/ID pattern (singular, not plural)
                // Allow video indicators anywhere in path (not just at start)
                if (path.Contains("/video") || path.Contains("/videos")) {
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

                // Direct video file URLs are allowed
                if (Constants.VideoExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) {
                    return true;
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
            if (string.IsNullOrWhiteSpace(html)) return null;
            
            try {
                Logger.Info($"[Pagination] Extracting from {currentUrl} (domain: {domain}, HTML: {html.Length} chars)");
                if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out Uri currentUri)) return null;
                
                var basePath = currentUri.AbsolutePath;
                var query = currentUri.Query;
                
                // Strategy 0: Rule34Video AJAX pagination (supports manual query params)
                if (domain.Contains("rule34video.com", StringComparison.OrdinalIgnoreCase)) {
                    bool isMember = currentUrl.Contains("/members/");
                    bool isPlaylist = currentUrl.Contains("/playlists/");

                    if (isMember || isPlaylist) {
                        Logger.Info($"[Pagination] Rule34Video Strategy 0 (AJAX) check. URL: {currentUrl}");
                        
                        // Rule34Video uses different parameter names depending on page type
                        string paramName = isMember ? "from_videos" : "from";
                        string dataKey = isMember ? "from_videos" : "from";

                        // Look for 'pager next' with data-parameters
                        // Matches both from: and from_videos: patterns
                        var nextLinkMatch = Regex.Match(html, @"pager\s+next[^>]*>.*?data-parameters=""[^""]*?" + dataKey + @":(\d+)""", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                        
                        if (nextLinkMatch.Success) {
                            int nextOffset = int.Parse(nextLinkMatch.Groups[1].Value);
                            var baseUri = currentUrl.Split('?')[0];
                            var finalNext = $"{baseUri}?{paramName}={nextOffset}";
                            
                            Logger.Info($"[Pagination] SUCCESS: Detected AJAX pagination for Rule34Video. Target offset: {nextOffset}. URL: {finalNext}");
                            return finalNext;
                        }

                        // Fallback: If no explicit 'next' pager, look for any page link with an offset higher than current
                        var currentFromMatch = Regex.Match(currentUrl, paramName + @"=(\d+)");
                        int currentFromValue = currentFromMatch.Success ? int.Parse(currentFromMatch.Groups[1].Value) : 1;
                        
                        var anyPageMatch = Regex.Matches(html, dataKey + @":(\d+)");
                        foreach (Match m in anyPageMatch) {
                            int offset = int.Parse(m.Groups[1].Value);
                            if (offset > currentFromValue) {
                                var baseUri = currentUrl.Split('?')[0];
                                var finalNext = $"{baseUri}?{paramName}={offset}";
                                Logger.Info($"[Pagination] SUCCESS: Following non-pager link to offset {offset}. URL: {finalNext}");
                                return finalNext;
                            }
                        }
                    }
                }

                // Strategy 1: Look for "next" link in pagination
                var nextLinkPatterns = new[] {
                    @"<a[^>]+href\s*=\s*[""']([^""']+)[""'][^>]*>(?:(?!</a>).)*?\s*(?:next|>|»|fa-chevron-right|chevron-right)\s*<",
                    @"<a[^>]+href\s*=\s*[""']([^""']+)[""'][^>]*>(?:(?!</a>).)*?chevron(?:(?!</a>).)*?</a>",
                    @"rel\s*=\s*[""']next[""'][^>]*href\s*=\s*[""']([^""']+)[""']",
                    @"href\s*=\s*[""']([^""']+)[""'][^>]*rel\s*=\s*[""']next[""']",
                };
                
                foreach (var pattern in nextLinkPatterns) {
                    var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (match.Success && match.Groups.Count > 1) {
                        var nextUrl = match.Groups[1].Value;
                        var resolved = ResolveUrl(nextUrl, currentUrl);
                        if (resolved != null && Uri.TryCreate(resolved, UriKind.Absolute, out Uri nextUri)) {
                            var normalizedNextPath = nextUri.AbsolutePath.TrimEnd('/');
                            var normalizedBasePath = basePath.TrimEnd('/');
                            
                            // Rule34Video specific: don't follow favorites pagination
                            if (domain.Contains("rule34video.com", StringComparison.OrdinalIgnoreCase)) {
                                if (nextUrl.Contains("#fav_videos", StringComparison.OrdinalIgnoreCase) || 
                                    normalizedNextPath.Contains("/favourites/", StringComparison.OrdinalIgnoreCase)) {
                                    Logger.Info($"[Pagination] Rejecting favorites-related URL on Rule34Video: {resolved}");
                                    continue;
                                }
                            }

                            // Reject if it's the homepage or root path
                            if (string.IsNullOrEmpty(normalizedNextPath) || normalizedNextPath == "/") {
                                Logger.Info($"[Pagination] Rejecting root URL as next page: {resolved}");
                                continue;
                            }
                            
                            // Ensure same domain
                            if (!nextUri.Host.ToLowerInvariant().Contains(domain.ToLowerInvariant())) {
                                continue;
                            }
                            
                            // Ensure it's actually a pagination of the same content
                            // Either: next path starts with base path (typical pagination)
                            // OR: same first path segment (e.g., /playlist/ -> /playlist/)
                            var baseSegments = normalizedBasePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                            var nextSegments = normalizedNextPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                            
                            bool isValidNextPage = false;
                            
                            // Must have at least same first segment
                            if (baseSegments.Length > 0 && nextSegments.Length > 0 && 
                                baseSegments[0].Equals(nextSegments[0], StringComparison.OrdinalIgnoreCase)) {
                                isValidNextPage = true;
                            }
                            
                            // Also check that it's not the same URL
                            if (resolved == currentUrl) {
                                isValidNextPage = false;
                            }
                            
                            if (isValidNextPage) {
                                Logger.Info($"[Pagination] Found next page URL using Strategy 1: {resolved}");
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
                }
                
                return null; // No next page found
            } catch (Exception ex) {
                Logger.Warning($"Error extracting next page URL: {ex.Message}");
                return null;
            }
        }

    }
}



