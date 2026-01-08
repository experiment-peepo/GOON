using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GOON.Windows;
using GOON.ViewModels;
using System.Diagnostics;

namespace GOON.Classes {
    /// <summary>
    /// Service for managing video playback across multiple screens
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public class VideoPlayerService {
        readonly List<HypnoWindow> players = new List<HypnoWindow>();
        private readonly object _playersLock = new object();
        public System.Collections.ObjectModel.ObservableCollection<ActivePlayerViewModel> ActivePlayers { get; } = new System.Collections.ObjectModel.ObservableCollection<ActivePlayerViewModel>();
        private readonly LruCache<string, bool> _fileExistenceCache;
        private System.Windows.Threading.DispatcherTimer _masterSyncTimer;

        /// <summary>
        /// Event raised when a media error occurs during playback
        /// </summary>
        public event EventHandler<string> MediaErrorOccurred;

        public VideoPlayerService() {
            var ttl = TimeSpan.FromMinutes(Constants.CacheTtlMinutes);
            _fileExistenceCache = new LruCache<string, bool>(Constants.MaxFileCacheSize, ttl);

            _masterSyncTimer = new System.Windows.Threading.DispatcherTimer();
            _masterSyncTimer.Interval = TimeSpan.FromMilliseconds(100);
            _masterSyncTimer.Tick += MasterSyncTimer_Tick;
        }

        private DateTime _lastSessionSave = DateTime.MinValue;

        private void MasterSyncTimer_Tick(object sender, EventArgs e) {
            // ... (Existing sync logic) ...
            
            // Auto-save session state every 5 seconds
            if ((DateTime.Now - _lastSessionSave).TotalSeconds > 5) {
                SaveSessionState();
                _lastSessionSave = DateTime.Now;
            }

            List<HypnoWindow> playersSnapshot;
            lock (_playersLock) {
                if (players.Count == 0) return;
                playersSnapshot = players.ToList();
            }

            // Group players by current source to sync them
            var groups = playersSnapshot
                .Where(p => p.ViewModel?.CurrentSource != null)
                .GroupBy(p => p.ViewModel.CurrentSource.ToString());

            foreach (var group in groups) {
                var playerList = group.ToList();
                if (playerList.Count == 0) continue;

                // --- 1. Ready Check Phase ---
                // Check if all players in this group are either Playing or Ready
                var allReady = playerList.All(p => p.ViewModel.MediaState == System.Windows.Controls.MediaState.Play || p.ViewModel.IsReady);
                
                if (allReady) {
                    // Triggers playback for any players that are waiting in the Ready state
                    var waitingPlayers = playerList.Where(p => p.ViewModel.IsReady).ToList();
                    foreach (var p in waitingPlayers) {
                        p.ViewModel.ForcePlay();
                    }
                }

                // --- 2. Synchronization Phase ---
                if (playerList.Count < 2) continue; // No sync needed for single monitors

                // Only sync players that are actually playing and have reported a position/timestamp
                var activePlayers = playerList
                    .Where(p => p.ViewModel.MediaState == System.Windows.Controls.MediaState.Play && 
                                p.ViewModel.LastPositionRecord.timestamp > 0)
                    .ToList();

                if (activePlayers.Count < 2) continue;

                // Use the first active player in the group as the master
                var master = activePlayers[0];
                var masterRecord = master.ViewModel.LastPositionRecord;
                
                // Calculate master's "virtual current position" by adding elapsed time since the record was taken
                var elapsedSinceRecord = Stopwatch.GetElapsedTime(masterRecord.timestamp);
                var masterVirtualPos = masterRecord.position + elapsedSinceRecord;

                // Sync all other players in the group to the master's virtual position
                for (int i = 1; i < activePlayers.Count; i++) {
                    var follower = activePlayers[i];
                    
                    // Enforce single audio stream: Mute all followers
                    if (follower.ViewModel.Volume > 0) {
                        follower.ViewModel.Volume = 0;
                    }

                    var followerRecord = follower.ViewModel.LastPositionRecord;
                    
                    // Calculate follower's virtual position
                    var followerElapsed = Stopwatch.GetElapsedTime(followerRecord.timestamp);
                    var followerVirtualPos = followerRecord.position + followerElapsed;
                    
                    // Calculate drift (Master - Follower)
                    // If drift > 0, follower is BEHIND (needs to speed up)
                    // If drift < 0, follower is AHEAD (needs to slow down)
                    var drift = masterVirtualPos - followerVirtualPos;
                    var absDriftMs = Math.Abs(drift.TotalMilliseconds);
                    
                    if (absDriftMs < 100) {
                        // Zone 1: Sweet Spot (< 100ms)
                        // Increased tolerance to avoid fighting natural 4K jitter
                        if (follower.ViewModel.SpeedRatio != 1.0) {
                            follower.ViewModel.SpeedRatio = 1.0;
                        }
                    } else if (absDriftMs < 300) {
                        // Zone 2: Gentle Nudging (100ms - 300ms)
                        // Use only modest speed changes to prevent decoder strain
                        // Log only on state change to avoid spam
                        if (follower.ViewModel.SpeedRatio == 1.0) Logger.Info($"Nudging: Drift {absDriftMs:F0}ms. Micro-adjustment.");
                        
                        // Use very conservative speed changes for 4K stability
                        if (drift.TotalMilliseconds > 0) {
                            follower.ViewModel.SpeedRatio = 1.05; // 5% boost (safer for 4K60)
                        } else {
                            follower.ViewModel.SpeedRatio = 0.95; // 5% slow
                        }
                    } else {
                        // Zone 3: Hard Correct (> 300ms)
                        // Snap immediately if drift is noticeable
                        Logger.Info($"Drift {absDriftMs:F0}ms > 300ms threshold. Sharp seeking follower.");
                        follower.ViewModel.SyncPosition(masterVirtualPos);
                        follower.ViewModel.SpeedRatio = 1.0;
                    }
                }
            }
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
        public bool IsPlaying {
            get {
                lock (_playersLock) {
                    return players.Count > 0;
                }
            }
        }

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
                
                lock (_playersLock) {
                    players.Add(w);
                }
                ActivePlayers.Add(new ActivePlayerViewModel(sv.ToString(), w.ViewModel));
            }

            if (this.IsPlaying) {
                _masterSyncTimer.Start();
            }
        }

        /// <summary>
        /// Pauses all currently playing videos
        /// </summary>
        public void PauseAll() {
            List<HypnoWindow> playersSnapshot;
            lock (_playersLock) {
                playersSnapshot = players.ToList();
            }
            foreach (var w in playersSnapshot) w.ViewModel.Pause();
        }

        /// <summary>
        /// Resumes all paused videos
        /// </summary>
        public void ContinueAll() {
            List<HypnoWindow> playersSnapshot;
            lock (_playersLock) {
                playersSnapshot = players.ToList();
            }
            foreach (var w in playersSnapshot) w.ViewModel.Play();
        }

        /// <summary>
        /// Stops and disposes all video players
        /// </summary>
        public void StopAll() {
            // Unregister all screen hotkeys
            ActivePlayers.Clear();

            List<HypnoWindow> playersCopy;
            lock (_playersLock) {
                playersCopy = players.ToList();
                players.Clear();
            }
            
            foreach (var w in playersCopy) {
                try {
                    // Critical: Close should be on UI thread or at least handle it safely
                    if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == true) {
                        w.Close();
                    } else {
                        System.Windows.Application.Current?.Dispatcher.Invoke(() => w.Close());
                    }
                    
                    if (w is IDisposable disposable) {
                        disposable.Dispose();
                    }
                } catch (Exception ex) {
                    Logger.Warning("Error disposing window", ex);
                }
            }
            _masterSyncTimer.Stop();
        }

        /// <summary>
        /// Unregisters a single player when it's closed or failed
        /// </summary>
        internal void UnregisterPlayer(HypnoWindow player) {
            if (player == null) return;

            lock (_playersLock) {
                if (players.Remove(player)) {
                    Logger.Info($"[VideoPlayerService] Unregistered player for screen: {player.ScreenDeviceName}");
                }
                
                // Find and remove from ActivePlayers collection
                var vm = ActivePlayers.FirstOrDefault(ap => ap.Player == player.ViewModel);
                if (vm != null) {
                    ActivePlayers.Remove(vm);
                }

                if (players.Count == 0) {
                    _masterSyncTimer.Stop();
                    Logger.Info("[VideoPlayerService] Last player removed, stopped sync timer.");
                }
            }
        }



        /// <summary>
        /// Sets the volume for all video players
        /// </summary>
        /// <param name="volume">Volume level (0.0 to 1.0)</param>
        public void SetVolumeAll(double volume) {
            List<HypnoWindow> playersSnapshot;
            lock (_playersLock) {
                playersSnapshot = players.ToList();
            }
            foreach (var w in playersSnapshot) w.ViewModel.Volume = volume;
        }

        /// <summary>
        /// Sets the opacity for all video players
        /// </summary>
        /// <param name="opacity">Opacity level (0.0 to 1.0)</param>
        public void SetOpacityAll(double opacity) {
            List<HypnoWindow> playersSnapshot;
            lock (_playersLock) {
                playersSnapshot = players.ToList();
            }
            foreach (var w in playersSnapshot) w.ViewModel.Opacity = opacity;
        }

        /// <summary>
        /// Refreshes the opacity of all players (useful when AlwaysOpaque setting changes)
        /// </summary>
        public void RefreshAllOpacities() {
            List<HypnoWindow> playersSnapshot;
            lock (_playersLock) {
                playersSnapshot = players.ToList();
            }
            foreach (var w in playersSnapshot) {
                w.ViewModel.RefreshOpacity();
            }
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
        public async System.Threading.Tasks.Task PlayPerMonitorAsync(IDictionary<ScreenViewer, IEnumerable<VideoItem>> assignments, bool showGroupControl = true, PlaybackState resumeState = null) {
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
                
                // Enable Coordinated Start to prevent desync on startup
                // All players will pause at 0 and wait for the MasterSyncTimer to trigger them together
                w.ViewModel.UseCoordinatedStart = true;

                w.ViewModel.SetQueue(queue);
                
                // Apply Restore State if available
                if (resumeState != null) {
                    w.ViewModel.RestoreState(resumeState.CurrentIndex, resumeState.PositionTicks);
                    // Also restore speed if needed
                    if (resumeState.SpeedRatio != 1.0) w.ViewModel.SpeedRatio = resumeState.SpeedRatio;
                }


                lock (_playersLock) {
                    players.Add(w);
                }
                
                // If put into group control, don't add individual controls
                if (!sv.IsAllScreens && !showGroupControl) {
                    ActivePlayers.Add(new ActivePlayerViewModel(sv.ToString(), w.ViewModel));
                }
                
                // Stagger window creation to prevent simultaneous GPU resource allocation
                // This prevents Media Foundation from being overwhelmed when loading multiple high-res videos
                // 300ms delay allows each MediaElement to initialize before the next one starts
                await System.Threading.Tasks.Task.Delay(300);
            }

            // Consolidate "All Screens" players into a single control
            if (showGroupControl) {
                List<HypnoWindow> playersSnapshot;
                lock (_playersLock) {
                    playersSnapshot = players.ToList();
                }
                var allScreensPlayers = playersSnapshot.Where(p => p.ViewModel.UseCoordinatedStart).ToList();
                if (allScreensPlayers.Any()) {
                     var groupVm = new GroupHypnoViewModel(allScreensPlayers.Select(p => p.ViewModel));
                     ActivePlayers.Add(new ActivePlayerViewModel("All Monitors", groupVm));
                }
            }

            if (this.IsPlaying) {
                _masterSyncTimer.Start();
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

        private void SaveSessionState() {
            // Updated to use async save to prevent UI stutter (fire-and-forget)
            // No await here because this is called from a timer tick (void)
            try {
                if (App.Settings == null) return;

                List<HypnoWindow> playersSnapshot;
                lock (_playersLock) {
                    playersSnapshot = players.ToList();
                }

                if (playersSnapshot.Count == 0) return;

                // Grab the first active player to save as "Master" persistence
                var master = playersSnapshot.FirstOrDefault();
                if (master?.ViewModel == null) return;

                var (index, ticks, speed, paths) = master.ViewModel.GetPlaybackState();

                // Save only the lightweight state to settings.json
                var state = App.Settings.LastPlaybackState ?? new PlaybackState();
                state.CurrentIndex = index;
                state.PositionTicks = ticks;
                state.SpeedRatio = speed;
                state.LastPlayed = DateTime.Now;
                
                App.Settings.LastPlaybackState = state;
                
                // ASYNC SAVE: Fire and forget task to avoid blocking the UI thread
                // Catch exceptions inside the task to avoid unobserved task exceptions
                _ = System.Threading.Tasks.Task.Run(async () => {
                     try {
                        await App.Settings.SaveAsync();
                     } catch (Exception innerEx) {
                        Logger.Warning("Failed to auto-save session (async)", innerEx);
                     }
                });

            } catch (Exception ex) {
                Logger.Warning("Failed to initiate auto-save session", ex);
            }
        }

        public void ClearFileExistenceCache() {
            _fileExistenceCache.Clear();
        }
    }
}
