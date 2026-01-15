using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Controls;
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
        private readonly SharedClock _sharedClock = new SharedClock();

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
        private DateTime _lastSyncStallLog = DateTime.MinValue;

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

            // --- 0. Coordinated Group Index Sync ---
            // Ensure all players with the same SyncGroupId are playing the same video index.
            // This fixes divergence when Shuffle is on for "All Monitors" mode.
            var coordinatedGroups = playersSnapshot
                .Where(p => !string.IsNullOrEmpty(p.ViewModel.SyncGroupId))
                .GroupBy(p => p.ViewModel.SyncGroupId);

            foreach (var coordinatedGroup in coordinatedGroups) {
                var playerList = coordinatedGroup.ToList();
                if (playerList.Count <= 1) continue;

                var master = playerList[0];
                int masterIndex = master.ViewModel.CurrentIndex;
                
                // Only sync if the master has actually picked a valid video index
                if (masterIndex >= 0) {
                    foreach (var follower in playerList.Skip(1)) {
                        if (follower.ViewModel.CurrentIndex != masterIndex) {
                            Logger.Info($"[Sync] Coordinated player at {follower.ScreenDeviceName} diverged in group '{coordinatedGroup.Key}' (Index {follower.ViewModel.CurrentIndex} vs Master {masterIndex}). Correcting.");
                            follower.ViewModel.JumpToIndex(masterIndex);
                        }
                    }
                }
            }

            foreach (var group in groups) {
                var playerList = group.ToList();
                if (playerList.Count == 0) continue;

                // --- 1. Ready Check Phase ---
                // Flyleaf handles its own ready state, but we can check Status
                var allReady = playerList.All(p => p.ViewModel.Player.Status == FlyleafLib.MediaPlayer.Status.Playing || p.ViewModel.Player.Status == FlyleafLib.MediaPlayer.Status.Opening);
                
                if (allReady) {
                    // Triggers playback for any players that are waiting (Flyleaf usually plays on open if configured)
                    var waitingPlayers = playerList.Where(p => p.ViewModel.Player.Status == FlyleafLib.MediaPlayer.Status.Opening).ToList();
                    foreach (var p in waitingPlayers) {
                         // No explicit action needed for Coordinated Start yet, will be handled by Clock in Phase 4
                    }
                }

                // --- 2. Synchronization Phase ---
                // We sync groups of 2+ players, OR single players using ExternalClock (to handle AutoPlay=false start)
                if (playerList.Count < 2 && !playerList.Any(p => p.ViewModel.ExternalClock != null)) continue;

                // --- 2a. Flyleaf External Clock Sync (Automatic) ---
                // If any player in the group uses ExternalClock, we rely on its internal sync.
                // We still need to enforce muting of followers.
                if (playerList.Any(p => p.ViewModel.ExternalClock != null)) {
                    var clock = _sharedClock;
                    
                    bool anyBuffering = playerList.Any(p => p.ViewModel.Player.IsBuffering || p.ViewModel.Player.Status == FlyleafLib.MediaPlayer.Status.Opening);
                    bool allOpening = playerList.All(p => p.ViewModel.Player.Status == FlyleafLib.MediaPlayer.Status.Opening);
                    
                    // A player is "buffered enough" if it's Playing, OR if it's Ready (paused at first frame), 
                    // AND it's not currently in the specific 'Opening' or 'IsBuffering' states.
                    bool allBuffered = playerList.All(p => 
                        (p.ViewModel.IsReady || p.ViewModel.Player.Status == FlyleafLib.MediaPlayer.Status.Playing) && 
                        !p.ViewModel.Player.IsBuffering && 
                        p.ViewModel.Player.Status != FlyleafLib.MediaPlayer.Status.Opening);

                    if (allOpening && clock.Ticks > 0) {
                        Logger.Info($"[Sync] All players opening/restarting. Resetting SharedClock.");
                        clock.Reset();
                    }

                    if (clock.IsRunning) {
                        if (anyBuffering) {
                            var bufferingPlayers = playerList
                                .Where(p => p.ViewModel.Player.IsBuffering || p.ViewModel.Player.Status == FlyleafLib.MediaPlayer.Status.Opening)
                                .Select(p => $"{p.ScreenDeviceName} ({(p.ViewModel.Player.IsBuffering ? "Buffering" : "Opening")})");
                            
                            Logger.Info($"[Sync] Pausing SharedClock. Stalled players: [{string.Join(", ", bufferingPlayers)}]");
                            clock.Pause();
                        } else {
                            // Ensure all ready players are playing if clock is running
                            foreach (var p in playerList) {
                                if (p.ViewModel.MediaState == MediaState.Pause && p.ViewModel.IsReady) {
                                    Logger.Info($"[Sync] Resuming {p.ScreenDeviceName} to match running SharedClock.");
                                    p.ViewModel.Play();
                                }
                            }
                        }
                    } else if (allBuffered && playerList.Any()) {
                        // If everyone is ready/buffered and the clock is stopped, START IT.
                        // This handles the "Auto-play after skip" requirement.
                        Logger.Info($"[Sync] All players ready/buffered. Starting/Resuming SharedClock. (Ticks: {clock.Ticks})");
                        clock.Start();
                        foreach (var p in playerList) {
                            if (p.ViewModel.MediaState != MediaState.Play) {
                                p.ViewModel.Play();
                            }
                        }
                    } else if (!clock.IsRunning) {
                        // Throttled logging to avoid flooding the log file
                        if ((DateTime.Now - _lastSyncStallLog).TotalSeconds > 5) {
                            var states = string.Join(", ", playerList.Select(p => 
                                $"{p.ScreenDeviceName}: Ready={p.ViewModel.IsReady}, Sts={p.ViewModel.Player.Status}, Buf={p.ViewModel.Player.IsBuffering}"));
                            Logger.Debug($"[Sync-Stall] Clock Stopped. Waiting for: {states}");
                            _lastSyncStallLog = DateTime.Now;
                        }
                    }

                    // Sync speed across all players in the group
                    foreach (var p in playerList) {
                        if (Math.Abs(p.ViewModel.SpeedRatio - clock.Speed) > 0.01) {
                            Logger.Info($"[Sync] Syncing speed for {p.ScreenDeviceName} to {clock.Speed}");
                            p.ViewModel.SpeedRatio = clock.Speed;
                        }
                    }

                    var primary = playerList.FirstOrDefault();
                    foreach (var p in playerList) {
                        if (p != primary && p.ViewModel.Volume > 0) {
                            Logger.Info($"[Sync] Muting follower on {p.ScreenDeviceName}");
                            p.ViewModel.Volume = 0;
                        }
                    }
                    continue; // Skip legacy drift correction
                }

                // --- 2b. Legacy Sync (for single/uncoordinated players, if any) ---
                // ... (rest of legacy sync if needed, but for now we skip)
                continue;
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
            _sharedClock.Pause();
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
            _sharedClock.Start();
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
            _masterSyncTimer?.Stop();
            _sharedClock.Pause();
            _sharedClock.Reset();

            // SESSION RESUME: Immediate save of positions when session ends
            PlaybackPositionTracker.Instance.SaveSync();

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
        /// Refreshes the Super Resolution setting of all players
        /// </summary>
        public void RefreshAllSuperResolution() {
            List<HypnoWindow> playersSnapshot;
            lock (_playersLock) {
                playersSnapshot = players.ToList();
            }
            foreach (var w in playersSnapshot) {
                w.ViewModel.RefreshSuperResolution();
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
            
            int sharedCoordinatedIndex = -1;
            
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
                if (showGroupControl) {
                    w.ViewModel.SyncGroupId = "AllMonitors";
                }

                w.ViewModel.SetQueue(queue);
                w.ViewModel.ExternalClock = _sharedClock;
                
                if (!string.IsNullOrEmpty(w.ViewModel.SyncGroupId)) {
                    if (sharedCoordinatedIndex == -1) {
                        // First player in the group picks the random starting point
                        sharedCoordinatedIndex = w.ViewModel.CurrentIndex;
                    } else {
                        // Subsequent players follow the first one immediately
                        w.ViewModel.JumpToIndex(sharedCoordinatedIndex);
                        
                        // MUTE FOLLOWERS IMMEDIATELY
                        w.ViewModel.Volume = 0;
                        Logger.Info($"[Sync] Initialized follower on {w.ScreenDeviceName} as muted.");
                    }
                }

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
                
                // Stagger window creation slightly (reduced for Flyleaf)
                await System.Threading.Tasks.Task.Delay(100);
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
