using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using YoutubeDLSharp;

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
                
                var result = await _ytdl.RunVideoDataFetch(url, ct: cancellationToken);
                
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

        public void Dispose() {
            // YoutubeDL doesn't require disposal
            _isAvailable = false;
        }
    }
}
