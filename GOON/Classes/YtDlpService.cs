using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDLSharp;
using YoutubeDLSharp.Options;

namespace GOON.Classes {
    /// <summary>
    /// Service for extracting video URLs using yt-dlp
    /// Simplified version focusing on URL extraction only
    /// </summary>
    public class YtDlpService : IDisposable {
        private readonly YoutubeDL _ytdl;
        private bool _isAvailable;
        private readonly string _ytDlpPath;

        public YtDlpService(string ytDlpPath = null) {
            // Try to find yt-dlp.exe in common locations
            _ytDlpPath = ytDlpPath ?? FindYtDlpExecutable();
            
            if (string.IsNullOrEmpty(_ytDlpPath)) {
                Logger.Warning("yt-dlp.exe not found. Video extraction will fall back to scraping.");
                _isAvailable = false;
                return;
            }

            try {
                _ytdl = new YoutubeDL();
                _ytdl.YoutubeDLPath = _ytDlpPath;
                _ytdl.OutputFolder = Path.GetTempPath();
                _isAvailable = true;
                Logger.Info($"yt-dlp service initialized: {_ytDlpPath}");
            } catch (Exception ex) {
                Logger.Error("Failed to initialize yt-dlp service", ex);
                _isAvailable = false;
            }
        }

        public bool IsAvailable => _isAvailable;

        /// <summary>
        /// Extracts the best quality video URL from a page URL
        /// </summary>
        public async Task<string> GetBestVideoUrlAsync(string url, CancellationToken cancellationToken = default) {
            if (!_isAvailable) return null;

            try {
                Logger.Info($"[yt-dlp] Extracting video URL: {url}");
                
                // Check if we should use cookies for this URL
                OptionSet options = null;
                
                var result = await _ytdl.RunVideoDataFetch(url, ct: cancellationToken, overrideOptions: options);
                
                if (!result.Success) {
                    Logger.Warning($"[yt-dlp] Extraction failed: {result.ErrorOutput?.FirstOrDefault()}");
                    return null;
                }

                var videoUrl = result.Data?.Url;
                if (!string.IsNullOrEmpty(videoUrl)) {
                    Logger.Info($"[yt-dlp] Successfully extracted URL");
                }
                
                return videoUrl;
            } catch (Exception ex) {
                Logger.Warning($"[yt-dlp] Exception during extraction: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extracts video title and basic metadata
        /// </summary>
        public async Task<YtDlpVideoInfo> ExtractVideoInfoAsync(string url, CancellationToken cancellationToken = default) {
            if (!_isAvailable) return null;

            try {
                Logger.Info($"[yt-dlp] Extracting video info: {url}");
                
                var result = await _ytdl.RunVideoDataFetch(url, ct: cancellationToken);
                
                if (!result.Success || result.Data == null) {
                    Logger.Warning($"[yt-dlp] Info extraction failed: {result.ErrorOutput?.FirstOrDefault()}");
                    return null;
                }

                var data = result.Data;
                var info = new YtDlpVideoInfo {
                    Url = data.Url ?? string.Empty,
                    Title = data.Title ?? "Unknown",
                    Duration = (int)(data.Duration ?? 0),
                    Thumbnail = data.Thumbnail,
                    Description = data.Description,
                    Uploader = data.Uploader
                };

                Logger.Info($"[yt-dlp] Successfully extracted info: {info.Title}");
                return info;
            } catch (Exception ex) {
                Logger.Warning($"[yt-dlp] Exception during info extraction: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Finds yt-dlp.exe in common locations
        /// </summary>
        private string FindYtDlpExecutable() {
            // Check in application directory
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var paths = new[] {
                Path.Combine(appDir, "yt-dlp.exe"),
                Path.Combine(appDir, "bin", "yt-dlp.exe"),
                Path.Combine(appDir, "tools", "yt-dlp.exe"),
                "yt-dlp.exe" // Check PATH
            };

            foreach (var path in paths) {
                if (File.Exists(path)) {
                    return Path.GetFullPath(path);
                }
            }

            // Try to find in PATH
            try {
                var pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(pathEnv)) {
                    foreach (var dir in pathEnv.Split(';')) {
                        var fullPath = Path.Combine(dir.Trim(), "yt-dlp.exe");
                        if (File.Exists(fullPath)) {
                            return fullPath;
                        }
                    }
                }
            } catch {
                // Ignore path search errors
            }

            return null;
        }

        /// <summary>
        /// Creates a temporary cookie file in Netscape format from a cookie string
        /// Cookie string format: name1=value1; name2=value2
        /// </summary>
        private string CreateTempCookieFile(string cookieString, string domain) {
            try {
                var tempFile = Path.Combine(Path.GetTempPath(), $"goon_cookies_{domain.Replace(".", "_")}.txt");
                var lines = new System.Collections.Generic.List<string> {
                    "# Netscape HTTP Cookie File",
                    "# https://curl.haxx.se/docs/http-cookies.html"
                };
                
                // Parse cookies from string format: name=value; name2=value2
                var cookies = cookieString.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                // Netscape format: domain\tflag\tpath\tsecure\texpiration\tname\tvalue
                // Ensure the domain is correctly formatted
                // Use both dotted and non-dotted to be safe
                var dottedDomain = domain.StartsWith(".") ? domain : "." + domain;
                var exactDomain = domain.TrimStart('.');
                
                foreach (var cookie in cookies) {
                    var trimmed = cookie.Trim();
                    var eqIndex = trimmed.IndexOf('=');
                    if (eqIndex > 0) {
                        var name = trimmed.Substring(0, eqIndex).Trim();
                        var value = trimmed.Substring(eqIndex + 1).Trim();
                        
                        // Add both versions – yt-dlp/curl can be picky
                        lines.Add($"{dottedDomain}\tTRUE\t/\tTRUE\t2147483647\t{name}\t{value}");
                        lines.Add($"{exactDomain}\tFALSE\t/\tTRUE\t2147483647\t{name}\t{value}");
                    }
                }
                
                File.WriteAllLines(tempFile, lines);
                return tempFile;
            } catch (Exception ex) {
                Logger.Warning($"[yt-dlp] Failed to create temp cookie file: {ex.Message}");
                return null;
            }
        }

        public void Dispose() {
            // YoutubeDL doesn't require disposal
            _isAvailable = false;
        }
    }
}
