using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using TrainMeX.Windows;
using TrainMeX.ViewModels;

namespace TrainMeX.Classes {
    /// <summary>
    /// Service for managing video playback across multiple screens
    /// </summary>
    public class VideoPlayerService {
        readonly List<HypnoWindow> players = new List<HypnoWindow>();
        private readonly LruCache<string, bool> _fileExistenceCache;

        /// <summary>
        /// Event raised when a media error occurs during playback
        /// </summary>
        public event EventHandler<string> MediaErrorOccurred;

        public VideoPlayerService() {
            var ttl = TimeSpan.FromMinutes(Constants.CacheTtlMinutes);
            _fileExistenceCache = new LruCache<string, bool>(Constants.MaxFileCacheSize, ttl);
        }

        /// <summary>
        /// Called by HypnoWindow when a media error occurs
        /// </summary>
        internal void OnMediaError(string errorMessage) {
            MediaErrorOccurred?.Invoke(this, errorMessage);
        }

        /// <summary>
        /// Gets whether any videos are currently playing
        /// </summary>
        public bool IsPlaying => players.Count > 0;

        /// <summary>
        /// Plays videos on the specified screens
        /// </summary>
        /// <param name="files">Video files to play</param>
        /// <param name="screens">Screens to play on</param>
        public async System.Threading.Tasks.Task PlayOnScreensAsync(IEnumerable<VideoItem> files, IEnumerable<ScreenViewer> screens) {
            StopAll();
            var queue = await NormalizeItemsAsync(files);
            var allScreens = Screen.AllScreens;
            
            foreach (var sv in screens ?? Enumerable.Empty<ScreenViewer>()) {
                // Validate screen still exists
                if (sv?.Screen == null) continue;
                bool screenExists = allScreens.Any(s => s.DeviceName == sv.Screen.DeviceName);
                
                if (!screenExists) {
                    Logger.Warning($"Screen {sv.DeviceName} is no longer available, skipping");
                    continue;
                }
                
                var w = new HypnoWindow(sv.Screen);
                w.Show();
                
                w.ViewModel.SetQueue(queue); 
                
                players.Add(w);
            }
        }

        /// <summary>
        /// Pauses all currently playing videos
        /// </summary>
        public void PauseAll() {
            foreach (var w in players) w.ViewModel.Pause();
        }

        /// <summary>
        /// Resumes all paused videos
        /// </summary>
        public void ContinueAll() {
            foreach (var w in players) w.ViewModel.Play();
        }

        /// <summary>
        /// Stops and disposes all video players
        /// </summary>
        public void StopAll() {
            // Create a copy of the list to avoid modification during iteration
            var playersCopy = players.ToList();
            players.Clear();
            
            foreach (var w in playersCopy) {
                try {
                    w.Close();
                    if (w is IDisposable disposable) {
                        disposable.Dispose();
                    }
                } catch (Exception ex) {
                    Logger.Warning("Error disposing window", ex);
                }
            }
        }

        /// <summary>
        /// Sets the volume for all video players
        /// </summary>
        /// <param name="volume">Volume level (0.0 to 1.0)</param>
        public void SetVolumeAll(double volume) {
            foreach (var w in players) w.ViewModel.Volume = volume;
        }

        /// <summary>
        /// Sets the opacity for all video players
        /// </summary>
        /// <param name="opacity">Opacity level (0.0 to 1.0)</param>
        public void SetOpacityAll(double opacity) {
            foreach (var w in players) w.ViewModel.Opacity = opacity;
        }

        /// <summary>
        /// Applies the prevent minimize setting to all windows
        /// </summary>
        public void ApplyPreventMinimizeSetting() {
            // Settings are applied via StateChanged event handler in HypnoWindow
            // No action needed here as the setting is checked in real-time
        }

        /// <summary>
        /// Plays videos on specific monitors with per-monitor assignments
        /// </summary>
        /// <param name="assignments">Dictionary mapping screens to their video playlists</param>
        public async System.Threading.Tasks.Task PlayPerMonitorAsync(IDictionary<ScreenViewer, IEnumerable<VideoItem>> assignments) {
            StopAll();
            if (assignments == null) return;
            var allScreens = Screen.AllScreens;
            
            foreach (var kvp in assignments) {
                var sv = kvp.Key;
                
                // Validate screen still exists
                if (sv?.Screen == null) {
                    Logger.Warning("Screen viewer has null screen, skipping");
                    continue;
                }
                
                bool screenExists = allScreens.Any(s => s.DeviceName == sv.Screen.DeviceName);
                if (!screenExists) {
                    Logger.Warning($"Screen {sv.DeviceName} is no longer available, skipping");
                    continue;
                }
                
                var queue = await NormalizeItemsAsync(kvp.Value);
                if (!queue.Any()) continue;
                
                var w = new HypnoWindow(sv.Screen);
                w.Show();
                
                w.ViewModel.SetQueue(queue);

                players.Add(w);
            }
        }

        private async System.Threading.Tasks.Task<IEnumerable<VideoItem>> NormalizeItemsAsync(IEnumerable<VideoItem> files) {
            var list = new List<VideoItem>();
            foreach (var f in files ?? Enumerable.Empty<VideoItem>()) {
                if (f.IsUrl) {
                    // For URLs, just validate and add (no file existence check)
                    if (FileValidator.ValidateVideoUrl(f.FilePath, out _)) {
                        list.Add(f);
                    }
                } else if (Path.IsPathRooted(f.FilePath)) {
                    // For local files, check file existence
                    if (await CheckFileExists(f.FilePath).ConfigureAwait(false)) {
                        list.Add(f);
                    }
                }
            }
            return list;
        }

        private async Task<bool> CheckFileExists(string filePath) {
            // Use cache to avoid repeated disk I/O
            if (_fileExistenceCache.TryGetValue(filePath, out bool exists)) {
                return exists;
            }
            
            // Retry logic with exponential backoff
            exists = await CheckFileExistsWithRetry(filePath);
            _fileExistenceCache.Set(filePath, exists);
            return exists;
        }

        private async Task<bool> CheckFileExistsWithRetry(string filePath) {
            int attempt = 0;
            while (attempt < Constants.MaxRetryAttempts) {
                try {
                    return File.Exists(filePath);
                } catch (Exception ex) {
                    attempt++;
                    if (attempt >= Constants.MaxRetryAttempts) {
                        Logger.Warning($"Failed to check file existence after {Constants.MaxRetryAttempts} attempts: {filePath}", ex);
                        return false;
                    }
                    
                    // Exponential backoff with async delay
                    int delay = Constants.RetryBaseDelayMs * (int)Math.Pow(2, attempt - 1);
                    await Task.Delay(delay);
                }
            }
            return false;
        }

        /// <summary>
        /// Clears the file existence cache
        /// </summary>
        public void ClearFileExistenceCache() {
            _fileExistenceCache.Clear();
        }
    }
}
