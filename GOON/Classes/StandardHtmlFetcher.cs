using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace GOON.Classes {
    /// <summary>
    /// Standard implementation of IHtmlFetcher using HttpClient
    /// </summary>
    public class StandardHtmlFetcher : IHtmlFetcher {
        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        }) {
            Timeout = TimeSpan.FromSeconds(30)
        };
        
        private static readonly LruCache<string, string> _htmlCache = new LruCache<string, string>(100, TimeSpan.FromMinutes(2));

        static StandardHtmlFetcher() {
            // Set proper user agent to avoid being blocked
            _httpClient.DefaultRequestHeaders.UserAgent.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
            );
            
            // Accept gzip/deflate compression
            _httpClient.DefaultRequestHeaders.AcceptEncoding.Clear();
            _httpClient.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate");
            
            // Accept headers
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        }

        public async Task<string> FetchHtmlAsync(string url, CancellationToken cancellationToken = default) {
            try {
                if (string.IsNullOrWhiteSpace(url)) return null;

                // Check cache first
                if (_htmlCache.TryGetValue(url, out string cachedHtml)) {
                    Logger.Info($"Using cached HTML for: {url}");
                    return cachedHtml;
                }

                Logger.Info($"Fetching HTML from: {url}");
                var response = await _httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();
                var html = await response.Content.ReadAsStringAsync();
                Logger.Info($"Fetched {html.Length} characters from {url}");
                
                // Cache the result
                _htmlCache.Set(url, html);
                
                return html;
            } catch (HttpRequestException ex) {
                Logger.Error($"HTTP error fetching {url}: {ex.Message}");
                return null;
            } catch (TaskCanceledException) {
                // Return null on timeout/cancellation to avoid crashing callers
                return null;
            } catch (Exception ex) {
                Logger.Warning($"Unexpected error fetching {url}: {ex.Message}");
                return null;
            }
        }

        public async Task<string> ResolveRedirectUrlAsync(string url, string referer = null, CancellationToken cancellationToken = default) {
            try {
                Logger.Info($"Resolving redirect for: {url}");
                using (var request = new HttpRequestMessage(HttpMethod.Head, url)) {
                    if (!string.IsNullOrEmpty(referer)) {
                        request.Headers.Referrer = new Uri(referer);
                    }
                    
                    // We need to execute the request but handle redirects manually? 
                    // Actually HttpClient follows redirects by default.
                    // If we want the final URL, we let it follow and check the RequestMessage.RequestUri (which updates on redirect)
                    // OR we disable auto-redirect on the handler and inspect manually if we only want one hop.
                    // Since _httpClient is static and shared, we can't change AllowAutoRedirect easily per request.
                    // But typically HttpClient's response.RequestMessage.RequestUri contains the *final* URI after redirects.
                    
                    var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    // StandardHttpClient follows redirects. The Response uri should be the final one.
                    // Wait, response.RequestMessage.RequestUri is the final one.
                    
                    if (response.RequestMessage != null && response.RequestMessage.RequestUri != null) {
                        var finalUrl = response.RequestMessage.RequestUri.ToString();
                        Logger.Info($"Resolved to: {finalUrl}");
                        return finalUrl;
                    }
                }
                return url;
            } catch (Exception ex) {
                Logger.Warning($"Error resolving redirect for {url}: {ex.Message}");
                return url; // Fallback to original
            }
        }
    }
}
