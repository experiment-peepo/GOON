using System;
using System.Windows;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using GOON.Classes;
using FlyleafLib;
using FlyleafLib.MediaPlayer;
using Logger = GOON.Classes.Logger;

namespace GOON.ViewModels {
    public class HypnoViewModel : ObservableObject, IDisposable {
        private VideoItem[] _files;
        private int _currentPos = 0;
        private int _consecutiveFailures = 0;
        private const int MaxConsecutiveFailures = 10; // Stop retrying after 10 consecutive failures
        private ConcurrentDictionary<string, int> _fileFailureCounts = new ConcurrentDictionary<string, int>(); // Track failures per file (thread-safe)
        private const int MaxFailuresPerFile = 3; // Skip a file after 3 failures
        private bool _isLoading = false; // Prevent concurrent LoadCurrentVideo() calls
        private readonly object _loadLock = new object(); // Lock for loading operations
        private Uri _expectedSource = null; // Track the source we're expecting MediaOpened for
        private int _recursionDepth = 0; // Track recursion depth to prevent stack overflow
        private const int MaxRecursionDepth = 50; // Maximum recursion depth before aborting
        private CancellationTokenSource _loadCts; // Added to cancel ongoing URL extraction on Skip
        
        // Pre-buffering for instant playback
        private readonly IVideoDownloadService _downloadService;
        private CancellationTokenSource _preBufferCts = null;
        private string _preBufferedUrl = null; // The URL that was pre-buffered
        private string _preBufferedPath = null; // The local cache path for pre-buffered video
        
        public Config Config { get; private set; }
        public Player Player { get; private set; }
        
        private (TimeSpan position, long timestamp) _lastPositionRecord;
        private DateTime _lastSaveTime = DateTime.MinValue;
        public (TimeSpan position, long timestamp) LastPositionRecord {
            get => _lastPositionRecord;
            set => SetProperty(ref _lastPositionRecord, value);
        }

        public VideoItem CurrentItem => _currentItem;
        
        private Uri _currentSource;
        public Uri CurrentSource {
            get => _currentSource;
            set {
                SetProperty(ref _currentSource, value);
            }
        }

        private double _opacity;
        public virtual double Opacity {
            get => (App.Settings != null && App.Settings.AlwaysOpaque) ? 1.0 : _opacity;
            set {
                if (SetProperty(ref _opacity, value)) {
                    Logger.Debug($"[HypnoViewModel] Opacity changed: {value} (Effective: {Opacity})");
                }
            }
        }

        public void RefreshOpacity() {
            OnPropertyChanged(nameof(Opacity));
        }

        private double _volume;
        public virtual double Volume {
            get => _volume;
            set {
                if (SetProperty(ref _volume, value)) {
                    if (Player?.Audio != null) Player.Audio.Volume = (int)(value * 100); // Flyleaf uses 0-100
                    OnPropertyChanged(nameof(ActualVolume));
                }
            }
        }

        // MPV-style quadratic volume scaling
        // 100% UI volume (1.0) = 1.0^2 = 1.0 actual volume
        // Quadratic is better balanced: 50% slider = 25% power (vs 12.5% in cubic)
        public double ActualVolume => Math.Pow(_volume, 2);

        private double _speedRatio = 1.0;
        public virtual double SpeedRatio {
            get => _speedRatio;
            set {
                if (SetProperty(ref _speedRatio, value)) {
                    if (Player != null) Player.Speed = value; 
                }
            }
        }

        private MediaState _mediaState = MediaState.Manual;
        public MediaState MediaState {
            get => _mediaState;
            set => SetProperty(ref _mediaState, value);
        }

        private bool _isReady = false;
        public bool IsReady {
            get => _isReady;
            private set => SetProperty(ref _isReady, value);
        }

        public bool UseCoordinatedStart { get; set; } = false;
        public string SyncGroupId { get; set; } = null;
        
        public IClock ExternalClock {
            get => Player?.ExternalClock;
            set {
                if (Player != null) {
                    Player.ExternalClock = value;
                    if (value != null) {
                        Player.Config.Player.MasterClock = FlyleafLib.MediaPlayer.MasterClock.External;
                    }
                }
            }
        }

        public long SyncTolerance {
            get => Player?.Config.Player.SyncTolerance ?? 160000;
            set {
                if (Player != null) {
                    Player.Config.Player.SyncTolerance = value;
                    OnPropertyChanged(nameof(SyncTolerance));
                }
            }
        }

        public int ClockDriftMs => Player?.Video.ClockDriftMs ?? 0;
        public double D3DImageLatencyMs => Player?.Video.D3DImageLatencyMs ?? 0;
        
        public bool IsShuffle {
            get => App.Settings?.VideoShuffle ?? true;
            set {
                if (App.Settings != null) {
                    App.Settings.VideoShuffle = value;
                    OnPropertyChanged(nameof(IsShuffle));
                }
            }
        }
        
        public event EventHandler RequestPlay;
        public event EventHandler RequestPause;
        public event EventHandler RequestStop;
        public event EventHandler RequestStopBeforeSourceChange;
        public event EventHandler<MediaErrorEventArgs> MediaErrorOccurred;
        public event EventHandler<TimeSpan> RequestSyncPosition;
        public event EventHandler RequestReady;

        public ICommand SkipCommand { get; }
        public ICommand TogglePlayPauseCommand { get; }

        /// <summary>
        /// Event raised when the entire queue has failed and playback must stop
        /// </summary>
        public event EventHandler TerminalFailure;
        
        public void RefreshSuperResolution() {
            if (_disposed || Player == null) return;
            Player.Config.Video.SuperResolution = App.Settings?.EnableSuperResolution ?? false;
        }

        private readonly IVideoUrlExtractor _urlExtractor;

        public HypnoViewModel(IVideoUrlExtractor urlExtractor = null, IVideoDownloadService downloadService = null) {
            _urlExtractor = urlExtractor ?? (ServiceContainer.TryGet<IVideoUrlExtractor>(out var extractor) ? extractor : null) ?? App.UrlExtractor;
            _downloadService = downloadService ?? (ServiceContainer.TryGet<IVideoDownloadService>(out var ds) ? ds : null) ?? new VideoDownloadService();
            _opacity = 0.9; // Safe default to ensure window is visible during initial load
            Config = new Config();
            
            Config.Player.AutoPlay = false;
            Config.Player.MasterClock = MasterClock.Video; 
            Config.Player.Stats = true; // Enable telemetry stats

            // Increase buffering to prevent stutters on high-resolution/high-bitrate videos
            // 1 second = 10,000,000 ticks (100ns units)
            // Tuned for responsiveness: 1.5s total buffer.
            Config.Demuxer.BufferDuration = 15000000; 

            // Inject AI Super Resolution setting
            Config.Video.SuperResolution = App.Settings?.EnableSuperResolution ?? false;
            
            Player = new Player(Config);
            
            // Map Flyleaf events (Named handlers for safe unsubscription)
            Player.OpenCompleted  += Player_OpenCompleted;
            Player.PropertyChanged += Player_PropertyChanged;

            SkipCommand = new RelayCommand(_ => PlayNext(true));
            TogglePlayPauseCommand = new RelayCommand(_ => TogglePlayPause());
        }

        public virtual void TogglePlayPause() {
            if (_disposed) return;
            if (MediaState == MediaState.Play) {
                Pause();
            } else {
                Play();
            }
        }

        public int CurrentIndex => _currentPos;

        public void SetQueue(IEnumerable<VideoItem> files) {
            if (_disposed) return;
            // Unsubscribe from current item's PropertyChanged event to prevent memory leaks
            // This must be done before changing the queue to ensure proper cleanup
            lock (_loadLock) {
                if (_currentItem != null) {
                    _currentItem.PropertyChanged -= CurrentItem_PropertyChanged;
                    _currentItem = null;
                }
                // Reset loading state when queue changes
                _isLoading = false;
                // Reset recursion depth when queue changes
                _recursionDepth = 0;
            }
            
            // Materialize to array for indexed access - this is necessary for PlayNext() logic
            _files = files?.ToArray() ?? Array.Empty<VideoItem>();
            _currentPos = -1;
            
            Logger.Info($"Queue updated with {_files.Length} videos");
            foreach (var f in _files) {
                Logger.Info($"  - {f.FileName} ({(f.IsUrl ? "URL" : "Local")})");
            }
            
            // Clear failure track when starting a new queue
            _fileFailureCounts.Clear();
            _consecutiveFailures = 0;
            
            // Cancel any pending pre-buffer when queue changes
            _preBufferCts?.Cancel();
            _preBufferedUrl = null;
            _preBufferedPath = null;
            
            // Start playing the new queue
            PlayNext();
        }



        public void JumpToIndex(int index) {
            if (_files == null || index < 0 || index >= _files.Length) return;
            if (_currentPos == index) return;

            Logger.Info($"Jumping to index {index} (requested for sync)");
            
            lock (_loadLock) {
                _isLoading = false; // Reset loading state to allow the new jump
                _recursionDepth = 0;
            }

            _currentPos = index;
            _ = LoadCurrentVideo();
        }

        private VideoItem _currentItem;

        public virtual async void PlayNext(bool force = false) {
            if (_disposed) return;
            if (_files == null || _files.Length == 0) return;

            // Prevent rapid/concurrent calls to PlayNext() while loading
            // This protects against race conditions when PlayNext() is called multiple times quickly
            lock (_loadLock) {
                if (_isLoading && !force) {
                    Logger.Warning("PlayNext() called while already loading, skipping to prevent race condition");
                    return;
                }
                
                if (force && _isLoading) {
                    Logger.Info("PlayNext forced: Interrupting current load to skip.");
                    _loadCts?.Cancel();
                    _isLoading = false;
                    _recursionDepth = 0; // Reset recursion depth on force skip
                    Stop(); // Interrupt current player if it was trying to open
                }
                
                _loadCts?.Cancel();
                _loadCts = new CancellationTokenSource();
                _isLoading = true; // Set loading flag early to prevent other PlayNext calls
            }

            // Find the next valid video that hasn't failed too many times
            int attempts = 0;
            
            do {
                if (IsShuffle && _files.Length > 1) {
                    // Smart Shuffle Logic
                    // 1. Identify valid candidates (unplayed)
                    var candidates = new System.Collections.Generic.List<int>();
                    var allIndices = Enumerable.Range(0, _files.Length).ToList();
                    
                    // Filter out played items
                    // We lock check against local file paths
                    var history = new HashSet<string>(App.Settings.PlayedHistory ?? new List<string>());
                    
                    foreach (var i in allIndices) {
                        if (_files[i] == null) continue;
                        var path = _files[i].FilePath;
                        if (!history.Contains(path)) {
                            candidates.Add(i);
                        }
                    }

                    // 2. If all played, reset history
                    if (candidates.Count == 0) {
                        Logger.Info("All videos played (Smart Shuffle). Resetting history loop.");
                        if (App.Settings.PlayedHistory != null) {
                            App.Settings.PlayedHistory.Clear();
                            App.Settings.Save();
                        }
                        candidates = allIndices; // Fallback to all
                    }

                    // 3. Pick random
                    if (candidates.Count > 0) {
                        var rnd = new Random();
                        _currentPos = candidates[rnd.Next(candidates.Count)];
                        
                        // 4. Record to history immediately
                        var pickedPath = _files[_currentPos]?.FilePath;
                        if (pickedPath != null && App.Settings.PlayedHistory != null) {
                            App.Settings.PlayedHistory.Add(pickedPath);
                            // Trim history if it gets too huge (optional, but good practice)
                            if (App.Settings.PlayedHistory.Count > 10000) {
                                App.Settings.PlayedHistory.RemoveRange(0, 1000); 
                            }
                            App.Settings.Save();
                        }
                    } else {
                        // Should not happen if _files.Length > 0
                        _currentPos = 0;
                    }

                } else {
                    // Sequential Logic
                    if (_currentPos + 1 < _files.Length) {
                        _currentPos++;
                    } else {
                        _currentPos = 0; // Loop
                    }
                }
                
                attempts++;
                
                // Prevent infinite loop if all files have failed
                if (attempts > _files.Length) {
                    Logger.Warning("All videos in queue have failed too many times. Stopping playback.");
                    MediaErrorOccurred?.Invoke(this, new MediaErrorEventArgs("All videos in queue have failed. Please check your video files."));
                    TerminalFailure?.Invoke(this, EventArgs.Empty);
                    lock (_loadLock) { _isLoading = false; }
                    return;
                }
                
                // Check if current file has failed too many times
                var item = _files[_currentPos];
                var currentPath = item?.FilePath;
                if (currentPath == null) continue;

                if (_fileFailureCounts.TryGetValue(currentPath, out int failures) && failures >= MaxFailuresPerFile) {
                    continue; // Skip this file, try next
                }

                // Deeper validation to avoid LoadCurrentVideo recursion
                bool isValid = true;
                if (item.IsUrl) {
                    if (!FileValidator.ValidateVideoUrl(currentPath, out _)) isValid = false;
                } else {
                    if (!Path.IsPathRooted(currentPath) || !File.Exists(currentPath)) isValid = false;
                }

                if (!isValid) {
                    // Mark as failed and continue
                    _fileFailureCounts.AddOrUpdate(currentPath, MaxFailuresPerFile, (k, v) => MaxFailuresPerFile);
                    
                    // Log telemetry
                    if (item.IsUrl) {
                        try {
                            var uri = new Uri(currentPath);
                            App.Telemetry?.LogUrlFailure(uri.Host);
                        } catch {
                            App.Telemetry?.LogUrlFailure("invalid-url");
                        }
                    } else {
                        App.Telemetry?.LogFormatFailure(System.IO.Path.GetExtension(currentPath));
                    }
                    continue;
                }
                
                break; // Found a valid file
            } while (true);

            await LoadCurrentVideo();
            Logger.Info($"Next video: #{_currentPos} - {_currentItem?.FileName ?? "Unknown"}");
        }

        private async Task LoadCurrentVideo() {
            if (_disposed) return;
            // Prevent concurrent calls to LoadCurrentVideo
            lock (_loadLock) {
                if (_isLoading && _currentItem != null) {
                    // This flag is already set by PlayNext or initial load
                }
                _isLoading = true; // Set flag inside lock to prevent race condition
                IsReady = false; // Reset ready flag as we are starting a new load
                
                // Check recursion depth to prevent stack overflow
                _recursionDepth++;
                if (_recursionDepth > MaxRecursionDepth) {
                    Logger.Error($"Maximum recursion depth ({MaxRecursionDepth}) exceeded in LoadCurrentVideo. Stopping playback.");
                    _isLoading = false;
                    _recursionDepth = 0;
                    MediaErrorOccurred?.Invoke(this, new MediaErrorEventArgs("Playback stopped due to excessive errors. Please check your video files."));
                    return;
                }
            }

            try {
                if (_currentItem != null) {
                    _currentItem.PropertyChanged -= CurrentItem_PropertyChanged;
                }

                if (_files == null || _files.Length == 0 || _currentPos < 0 || _currentPos >= _files.Length) {
                    lock (_loadLock) {
                        _isLoading = false;
                        _recursionDepth = Math.Max(0, _recursionDepth - 1); // Decrement on exit
                    }
                    return;
                }

                _currentItem = _files[_currentPos];
                _currentItem.PropertyChanged += CurrentItem_PropertyChanged;
                
                var path = _currentItem.FilePath;
                Logger.Info($"LoadCurrentVideo: Processing item '{_currentItem.FileName}' with path: {path}");
                
                // CRITICAL FIX: Detect and clean malformed Rule34Video URLs
                if (path.Contains("rule34video.com/video/") && path.Contains("/function/0/https://")) {
                    Logger.Warning($"LoadCurrentVideo: Detected malformed Rule34Video URL with page prefix. Attempting to clean...");
                    int httpIndex = path.IndexOf("https://", path.IndexOf("/function/0/"));
                    if (httpIndex > 0) {
                        var cleanedUrl = path.Substring(httpIndex);
                        Logger.Info($"LoadCurrentVideo: Cleaned URL from '{path}' to '{cleanedUrl}'");
                        path = cleanedUrl;
                    }
                }
                
                // Validate based on whether it's a URL or local file
                if (_currentItem.IsUrl) {
                    Logger.Info($"LoadCurrentVideo: Item is a URL, validating...");
                    // For URLs, validate URL format
                    if (!FileValidator.ValidateVideoUrl(path, out string urlValidationError)) {
                        Logger.Warning($"URL validation failed for '{_currentItem.FileName}': {urlValidationError}. Skipping to next video.");
                        lock (_loadLock) {
                            _isLoading = false;
                            _recursionDepth = Math.Max(0, _recursionDepth - 1);
                        }
                        PlayNext();
                        return;
                    }
                    Logger.Info($"LoadCurrentVideo: URL validation passed for: {path}");
                    
                    // RESOLVE PAGE URLS: If it's a page URL and not already cached, resolve it now
                    if (FileValidator.IsPageUrl(path)) {
                        var cached = _downloadService.GetCachedFilePath(path);
                        if (string.IsNullOrEmpty(cached) || cached.EndsWith(".partial")) {
                            Logger.Info($"LoadCurrentVideo: Page URL detected, resolving: {path}");
                            var token = _loadCts?.Token ?? CancellationToken.None;
                            var resolved = await _urlExtractor.ExtractVideoUrlAsync(path, token);
                            if (!string.IsNullOrEmpty(resolved)) {
                                Logger.Info($"LoadCurrentVideo: Successfully resolved page URL to: {resolved}");
                                path = resolved;
                            } else {
                                if (token.IsCancellationRequested) {
                                    Logger.Info("LoadCurrentVideo: Resolution cancelled.");
                                    return;
                                }
                                Logger.Warning($"LoadCurrentVideo: Failed to resolve page URL: {path}. Skipping.");
                                lock (_loadLock) {
                                    _isLoading = false;
                                    _recursionDepth = Math.Max(0, _recursionDepth - 1);
                                }
                                PlayNext();
                                return;
                            }
                        }
                    }

                    // Check if this URL is cached locally for instant playback
                    var cachedPath = _downloadService.GetCachedFilePath(path);
                    if (!string.IsNullOrEmpty(cachedPath) && !cachedPath.EndsWith(".partial")) {
                        // Concurrent Playback Safetey Check:
                        // If we are about to use a .downloading file (partial) AND we have a saved playback position,
                        // we must FORCE streaming instead. Seeking into a non-downloaded area of a local file fails/hangs,
                        // whereas streaming allows random access to non-buffered areas.
                        bool forceStream = false;
                        if (cachedPath.EndsWith(".downloading") && App.Settings?.RememberFilePosition == true) {
                             // Peeking at the tracking path for the CURRENT item (which is still the URL/PageURL)
                             var savedPos = PlaybackPositionTracker.Instance.GetPosition(_currentItem.TrackingPath);
                             if (savedPos.HasValue && savedPos.Value.TotalSeconds > 10) { // arbitrary buffer
                                 Logger.Info($"[ConcurrentPlayback] Active download detected but found saved position ({savedPos.Value:mm\\:ss}). Forcing stream for safe seeking.");
                                 forceStream = true;
                             }
                        }

                        if (!forceStream) {
                            Logger.Info($"[PreBuffer] Using cached file: {Path.GetFileName(cachedPath)}");
                            path = cachedPath;
                            // Change item behavior to treat as local file now
                            _currentItem = new VideoItem(cachedPath) {
                                Title = _currentItem.Title,
                                Opacity = _currentItem.Opacity,
                                Volume = _currentItem.Volume,
                                // CRITICAL: Preserve the OriginalPageUrl from the previous item or use the path if it was a page URL
                                OriginalPageUrl = !string.IsNullOrEmpty(_currentItem.OriginalPageUrl) ? _currentItem.OriginalPageUrl : (FileValidator.IsPageUrl(path) ? path : null)
                            };
                            _currentItem.PropertyChanged += CurrentItem_PropertyChanged;
                        }
                    }
                } else {
                    // For local files, check if path is rooted
                    if (!Path.IsPathRooted(path)) {
                        Logger.Warning($"Non-rooted path detected for '{_currentItem.FileName}': {path}. Skipping to next video.");
                        PlayNext();
                        return;
                    }
                    
                    // Re-validate file existence before attempting to load
                    if (!FileValidator.ValidateVideoFile(path, out string validationError) && !path.EndsWith(".downloading")) {
                        Logger.Warning($"File validation failed for '{_currentItem.FileName}': {validationError}. Skipping to next video.");
                        lock (_loadLock) {
                            _isLoading = false;
                            _recursionDepth = Math.Max(0, _recursionDepth - 1);
                        }
                        PlayNext();
                        return;
                    }
                }
                
                // Apply per-monitor/per-item settings
                Opacity = _currentItem.Opacity;
                Volume = _currentItem.Volume;
                
                RequestStopBeforeSourceChange?.Invoke(this, EventArgs.Empty);
                
                Uri newSource;
                if (_currentItem.IsUrl || path.StartsWith("http")) {
                    newSource = new Uri(path, UriKind.Absolute);
                } else {
                if (Path.IsPathRooted(path) && !path.StartsWith("http")) {
                   if (!File.Exists(path)) {
                       Logger.Error($"LoadCurrentVideo: File not found at path: {path}");
                   } else {
                       Logger.Info($"LoadCurrentVideo: File verified to exist: {path}");
                       
                       if (path.Contains('[') || path.Contains(']') || path.Contains('#') || path.Contains('%') || path.Contains(' ') || path.Any(c => c > 127)) {
                           string shortPath = GetShortPath(path);
                           if (!string.Equals(shortPath, path, StringComparison.OrdinalIgnoreCase)) {
                               Logger.Info($"LoadCurrentVideo: Converted problematic path '{path}' to short path '{shortPath}'");
                               path = shortPath;
                           }
                       }
                   }
                }

                if (path.Contains('#') || path.Contains('%')) {
                    try {
                        var uriBuilder = new UriBuilder {
                            Scheme = Uri.UriSchemeFile,
                            Host = string.Empty,
                            Path = Path.GetFullPath(path)
                        };
                        newSource = uriBuilder.Uri;
                    } catch (Exception ex) {
                        Logger.Warning($"Failed to create URI using UriBuilder for path: {path}. Falling back to standard constructor.", ex);
                        newSource = new Uri(Path.GetFullPath(path));
                    }
                } else {
                    newSource = new Uri(Path.GetFullPath(path));
                }
                
                Logger.Info($"LoadCurrentVideo: Generated URI: {newSource.AbsoluteUri}");
                } // End if(!IsUrl)
                
                lock (_loadLock) {
                    _expectedSource = newSource;
                    
                    if (App.Settings?.RememberFilePosition == true) {
                        var savedPos = PlaybackPositionTracker.Instance.GetPosition(_currentItem.TrackingPath);
                        if (savedPos.HasValue) {
                            Logger.Info($"[Resume] Found saved position for '{_currentItem.FileName}': {savedPos.Value:mm\\:ss}. Setting pending seek.");
                            _pendingSeekPosition = savedPos.Value;
                        }
                    }
                }
                
                if (CurrentSource != null && CurrentSource.Equals(newSource)) {
                    CurrentSource = null; // Clear source to force reload
                }
                
                try {
                    Config.Demuxer.UserAgent = App.Settings?.UserAgent;
                    Config.Demuxer.Cookies   = App.Settings?.Cookies;
                    
                    // CRITICAL: Inject Referer header for sites like Rule34Video
                    string referer = _currentItem.OriginalPageUrl;
                    
                    // If we resolved a Page URL locally, that page URL is the Referer
                    if (string.IsNullOrEmpty(referer) && FileValidator.IsPageUrl(_currentItem.FilePath)) {
                        referer = _currentItem.FilePath;
                    }

                    if (!string.IsNullOrEmpty(referer)) {
                        Logger.Info($"LoadCurrentVideo: Injecting Referer header: {referer}");
                        if (Config.Demuxer.FormatOpt == null) Config.Demuxer.FormatOpt = new System.Collections.Generic.Dictionary<string, string>();
                        Config.Demuxer.FormatOpt["headers"] = $"Referer: {referer}\r\n";
                    } else {
                        // Clear headers if no referer (to prevent leaking)
                         if (Config.Demuxer.FormatOpt != null && Config.Demuxer.FormatOpt.ContainsKey("headers")) {
                            Config.Demuxer.FormatOpt.Remove("headers");
                         }
                    }

                    Logger.Info($"LoadCurrentVideo: Opening path in Flyleaf: {path}");
                    CurrentSource = newSource;
                    Player.Open(path);
                    
                    // Monitor for "Opening" stall
                    _ = Task.Delay(10000).ContinueWith(t => {
                        if (_disposed) return;
                        if (_isLoading && Player.Status == Status.Opening) {
                            Logger.Warning($"LoadCurrentVideo: Player seems stuck in 'Opening' status for 10s. Path: {path}");
                        }
                    });
                } catch (Exception ex) {
                     Logger.Error($"LoadCurrentVideo: Error opening '{path}' in Flyleaf: {ex.Message}");
                     OnMediaFailed(ex);
                }
            } catch (Exception ex) {
                Logger.Error("Error in LoadCurrentVideo()", ex);
                lock (_loadLock) {
                    _isLoading = false;
                    _recursionDepth = Math.Max(0, _recursionDepth - 1);
                }
                PlayNext();
            }
        }

        private void CurrentItem_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (_currentItem == null) return;
            if (e.PropertyName == nameof(VideoItem.Opacity)) {
                Opacity = _currentItem.Opacity;
            } else if (e.PropertyName == nameof(VideoItem.Volume)) {
                Volume = _currentItem.Volume;
            }
        }

        private void Player_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (_disposed) return;
            
            if (e.PropertyName == nameof(Player.Status)) {
                if (Player.Status == FlyleafLib.MediaPlayer.Status.Ended) {
                    OnMediaEnded();
                }
            } 
            else if (e.PropertyName == nameof(Player.CurTime)) {
                // Update internal state record
                var position = TimeSpan.FromTicks(Player.CurTime);
                LastPositionRecord = (position, DateTime.Now.Ticks);
                
                // Save to persistent tracker every 3 seconds
                if ((DateTime.Now - _lastSaveTime).TotalSeconds >= 3 && _currentItem != null) {
                    SaveCurrentPosition();
                }
            }
        }

        private void SaveCurrentPosition() {
            if (_currentItem != null && Player != null) {
                var position = TimeSpan.FromTicks(Player.CurTime);
                PlaybackPositionTracker.Instance.UpdatePosition(_currentItem.TrackingPath, position);
                _lastSaveTime = DateTime.Now;
            }
        }

        private void Player_OpenCompleted(object sender, FlyleafLib.MediaPlayer.OpenCompletedArgs e) => OnMediaOpened(sender, e);

        public void OnMediaEnded() {
            if (_disposed) return;
            Logger.Info($"[HypnoViewModel] Media ended: {CurrentSource} (Pos: #{_currentPos})");
            
            _consecutiveFailures = 0; // Reset failure counter on successful playback
            
            // SESSION RESUME: Clear position so we don't resume at the very end next time
            if (App.Settings?.RememberFilePosition == true && _currentItem != null) {
                PlaybackPositionTracker.Instance.ClearPosition(_currentItem.TrackingPath);
            }

            // Clear failure count for this file since it played successfully (thread-safe)
            if (_currentItem != null) {
                if (_currentItem.FilePath != null) {
                    _fileFailureCounts.TryRemove(_currentItem.FilePath, out _);
                }
                
                // SESSION RESUME: Clear position so we don't resume at the very end next time
                if (App.Settings?.RememberFilePosition == true) {
                    PlaybackPositionTracker.Instance.ClearPosition(_currentItem.TrackingPath);
                }
            }
            
            // Reset recursion depth on successful completion
            lock (_loadLock) {
                _recursionDepth = 0;
            }
            
            PlayNext();
        }
        
        public void OnMediaOpened(object sender, FlyleafLib.MediaPlayer.OpenCompletedArgs e) {
            if (_disposed) return;
            if (!string.IsNullOrEmpty(e.Error)) {
                 OnMediaFailed(new Exception(e.Error));
                 return;
            }
            // Verify that the opened media matches what we're expecting
            // This prevents stale MediaOpened events from previous sources after SetQueue() changes
            lock (_loadLock) {
                // If CurrentSource doesn't match expected source, this is a stale event - ignore it
                if (_expectedSource == null || CurrentSource != _expectedSource) {
                    Logger.Warning("OnMediaOpened called for stale source, ignoring");
                    return;
                }
                
                // Reset loading flag when media successfully opens
                // This must be done in a lock to ensure thread safety
                _isLoading = false;
                // Reset recursion depth on successful load
                _recursionDepth = 0;
            }
            
            // Reset failure counter when video successfully opens
            _consecutiveFailures = 0;
            
            // Clear failure count for this file since it opened successfully (thread-safe)
            if (_currentItem?.FilePath != null) {
                _fileFailureCounts.TryRemove(_currentItem.FilePath, out _);
            }

            // Set initial parameters
            if (Player.Audio != null) {
                Player.Audio.Volume = (int)(_volume * 100);
                Logger.Info($"[HypnoViewModel] Applied volume {Player.Audio.Volume} to new media.");
            }
            Player.Speed = _speedRatio;
            
            if (UseCoordinatedStart) {
                // Coordinated start: Request PLAY to allow buffering to complete, 
                // but rely on the stopped SharedClock to keep it frozen at frame 0.
                // NOTE: We MUST call Play() here, not just set MediaState, to trigger the engine.
                Play();
                IsReady = true;
                RequestReady?.Invoke(this, EventArgs.Empty);
            } else {
                // Request play now that media is confirmed loaded
                // This ensures Play() is only called after MediaElement has processed the source
                Play();
            }
            
            // Handle pending seek (e.g., from RestoreState) - SEEK AFTER PLAY to ensure it applies
            if (_pendingSeekPosition.HasValue) {
                int seekMs = (int)_pendingSeekPosition.Value.TotalMilliseconds;
                Logger.Info($"[Resume] Seeking to saved position: {_pendingSeekPosition.Value:mm\\:ss} ({seekMs}ms)");
                Player.Seek(seekMs);
                _pendingSeekPosition = null;
            }

            // Start pre-buffering the next video for instant playback
            StartPreBuffer();
        }
        
        /// <summary>
        /// Starts pre-buffering the next video in the queue.
        /// Prioritizes 1080p+ videos and uses partial downloading for faster startup.
        /// </summary>
        private void StartPreBuffer() {
            if (_disposed) return;
            // Cancel any existing pre-buffer operation
            _preBufferCts?.Cancel();
            _preBufferCts = new CancellationTokenSource();
            var cancellationToken = _preBufferCts.Token;
            
            if (_files == null || _files.Length == 0) return;
            
            // Look ahead up to 3 videos and collect candidates for pre-buffering
            var candidates = new System.Collections.Generic.List<(VideoItem item, int quality, int position)>();
            for (int i = 1; i <= Math.Min(3, _files.Length); i++) {
                int pos = (_currentPos + i) % _files.Length;
                if (pos == _currentPos) continue; // Don't buffer current video
                
                var item = _files[pos];
                if (item?.IsUrl == true) {
                    var quality = QualitySelector.DetectQualityFromUrl(item.FilePath);
                    candidates.Add((item, quality, pos));
                }
            }
            
            if (candidates.Count == 0) return;
            
            // Prioritize 1080p+ videos, otherwise take the next one
            var highRes = candidates
                .Where(c => c.quality >= 1080)
                .OrderByDescending(c => c.quality)
                .FirstOrDefault();
            
            VideoItem nextItem;
            int detectedQuality;
            
            if (highRes.item != null) {
                nextItem = highRes.item;
                detectedQuality = highRes.quality;
                Logger.Info($"[PreBuffer] Prioritizing high-res video: {detectedQuality}p - {nextItem.FileName}");
            } else {
                var first = candidates.First();
                nextItem = first.item;
                detectedQuality = first.quality;
                Logger.Info($"[PreBuffer] No high-res found, buffering next: {(detectedQuality > 0 ? $"{detectedQuality}p" : "unknown")} - {nextItem.FileName}");
            }
            
            var videoUrl = nextItem.FilePath;
            
            // Already cached (full)?
            // We ignore .partial files here to ensure we resolve the URL correctly
            var cachedPath = _downloadService.GetCachedFilePath(videoUrl);
            if (!string.IsNullOrEmpty(cachedPath) && !cachedPath.EndsWith(".partial")) {
                _preBufferedUrl = videoUrl;
                _preBufferedPath = cachedPath;
                Logger.Info($"[PreBuffer] Already cached: {Path.GetFileName(cachedPath)}");
                return;
            }
            
            // Start background partial download for faster startup
            Logger.Info($"[PreBuffer] Starting partial download for: {nextItem.FileName}");
            _ = Task.Run(async () => {
                try {
                    if (_disposed) return;
                    var finalUrl = videoUrl;
                    
                    // If it's a page URL, resolve it first to avoid "Opening" stalls later
                    if (FileValidator.IsPageUrl(videoUrl)) {
                        Logger.Info($"[PreBuffer] Resolving site URL: {nextItem.FileName}");
                        var resolved = await _urlExtractor.ExtractVideoUrlAsync(videoUrl, cancellationToken);
                        if (!string.IsNullOrEmpty(resolved) && !cancellationToken.IsCancellationRequested) {
                            finalUrl = resolved;
                            // Update the item so LoadCurrentVideo picks it up immediately
                            nextItem.FilePath = resolved; 
                            Logger.Info($"[PreBuffer] Successfully resolved {nextItem.FileName}");
                        }
                    }

                    // Header preparation
                    var headers = new System.Collections.Generic.Dictionary<string, string>();
                    if (!string.IsNullOrEmpty(nextItem.OriginalPageUrl)) {
                        headers["Referer"] = nextItem.OriginalPageUrl;
                    }

                    // DOWNLOAD TO CACHE: For sites like Rule34Video with short-lived URLs, 
                    // we MUST download to disk immediately or extraction is wasted.
                    // We use DownloadVideoAsync (full) to avoid Flyleaf hitches with partial files.
                    // If the file is massive, it might take a while, but it's the most reliable way.
                    var localPath = await _downloadService.DownloadVideoAsync(finalUrl, headers, cancellationToken);
                    
                    if (_disposed) return;
                    if (!string.IsNullOrEmpty(localPath) && !cancellationToken.IsCancellationRequested) {
                        _preBufferedUrl = finalUrl;
                        _preBufferedPath = localPath;
                        Logger.Info($"[PreBuffer] Completed disk cache: {Path.GetFileName(localPath)}");
                    }
                } catch (OperationCanceledException) {
                    Logger.Info("[PreBuffer] Cancelled");
                } catch (Exception ex) {
                    Logger.Warning($"[PreBuffer] Failed: {ex.Message}");
                }
            }, cancellationToken);
        }

        public void OnMediaFailed(Exception ex) {
            if (_disposed) return;
            // Reset loading flag on failure
            // This must be done in a lock to ensure thread safety
            lock (_loadLock) {
                _isLoading = false;
            }
            
            var fileName = _currentItem?.FileName ?? "Unknown";
            var filePath = _currentItem?.FilePath;
            
            // Check for specific codec/media foundation errors
            bool isCodecError = false;
            bool isFileNotFoundError = false;
            bool isUrlOpenError = false;
            string specificAdvice = "";

            if (ex is COMException comEx) {
                // 0x8898050C = MILAVERR_UNEXPECTEDWMPFAILURE (Common with resource exhaustion or codec issues)
                // 0xC00D5212 = MF_E_TOPO_CODEC_NOT_FOUND (Explicit missing codec)
                // 0xC00D11B1 = NS_E_WMP_FILE_OPEN_FAILED (File/URL cannot be opened)
                uint errorCode = (uint)comEx.ErrorCode;
                if (errorCode == 0x8898050C) {
                    isCodecError = true;
                    specificAdvice = " This error (0x8898050C) typically indicates: 1) GPU/VRAM exhaustion when playing multiple videos, 2) Missing codecs, or 3) Corrupted video file. Try reducing the number of active screens or check if the file plays in other media players.";
                } else if (errorCode == 0xC00D5212) {
                    isCodecError = true;
                    specificAdvice = " Missing codec for this video format. Install required codecs or convert the video to a supported format.";
                } else if (errorCode == 0xC00D11B1) {
                    // File open failed - different handling for URLs vs local files
                    if (_currentItem?.IsUrl == true) {
                        isUrlOpenError = true;
                        specificAdvice = " URL cannot be opened. This typically means: 1) The URL has expired or is no longer valid, 2) Network connectivity issues, 3) The server is unavailable, or 4) DRM-protected content. Try refreshing the URL or checking your network connection.";
                        
                        // Clear cache for this page URL so it can be re-extracted on next attempt
                        if (!string.IsNullOrEmpty(_currentItem?.OriginalPageUrl)) {
                            Logger.Info($"[HypnoViewModel] Clearing cached URL for '{_currentItem.OriginalPageUrl}' due to playback failure.");
                            PersistentUrlCache.Instance.Remove(_currentItem.OriginalPageUrl);
                        }
                    } else {
                        isFileNotFoundError = true;
                        specificAdvice = " File cannot be opened. The file may be locked by another application, corrupted, or you may lack read permissions.";
                    }
                }
            } else if (ex is System.IO.FileNotFoundException) {
                isFileNotFoundError = true;
                // Check if file actually exists - this could be a URI encoding issue
                if (filePath != null && System.IO.File.Exists(filePath)) {
                    specificAdvice = " File exists on disk but MediaElement cannot load it. This may be due to special characters in the filename or path. Try renaming the file to remove special characters like '&', '#', etc.";
                } else {
                    specificAdvice = " File does not exist or has been moved/deleted.";
                }
            }
            
            var errorMessage = $"Failed to play video: {fileName}";
            
            Logger.Error(errorMessage, ex);
            
            // Increment failure counters
            _consecutiveFailures++;
            
            // Track failures per file (thread-safe with ConcurrentDictionary)
    if (filePath != null) {
        // If it's a known unrecoverable error, force max failures to skip immediately
        // Codec errors, file not found errors (if file REALLLY missing), and URL open errors should skip immediately
        bool isGenuinelyMissing = isFileNotFoundError && !System.IO.File.Exists(filePath);
        bool shouldMarkUnrecoverable = isCodecError || isGenuinelyMissing || isUrlOpenError;
        
        int increment = shouldMarkUnrecoverable ? MaxFailuresPerFile : 1;
        int failureCount = _fileFailureCounts.AddOrUpdate(filePath, increment, (key, oldValue) => oldValue + increment);
        
        if (shouldMarkUnrecoverable) {
            Logger.Warning($"Unrecoverable error for '{fileName}'. Marking as failed immediately to avoid retries.");
        } else {
            Logger.Warning($"File '{fileName}' has failed {failureCount} time(s). Will skip after {MaxFailuresPerFile} failures.");
        }
    }
            
            // Notify listeners (e.g., UI) about the error
            MediaErrorOccurred?.Invoke(this, new MediaErrorEventArgs($"{errorMessage}.{specificAdvice} Error: {ex?.Message ?? "Unknown error"}"));
            
            // Stop retrying if we've exceeded the failure threshold
            // This prevents infinite retry loops when all videos fail
            if (_consecutiveFailures >= MaxConsecutiveFailures) {
                Logger.Warning($"Stopped retrying after {MaxConsecutiveFailures} consecutive failures. All videos in queue may be invalid.");
                MediaErrorOccurred?.Invoke(this, new MediaErrorEventArgs($"Playback stopped after {MaxConsecutiveFailures} consecutive failures. Please check your video files."));
                TerminalFailure?.Invoke(this, EventArgs.Empty);
                return;
            }
            
            // Add a delay to allow GPU resources to free up (especially for 0x8898050C errors)
            int delayMs = isCodecError ? 500 : 300;
            _ = Task.Delay(delayMs).ContinueWith(_ => {
                if (_disposed) return;
                Application.Current?.Dispatcher.InvokeAsync(() => PlayNext());
            });
        }

        public virtual void Play() {
            MediaState = MediaState.Play;
            IsReady = false; // No longer just "Ready", actually playing
            Player.Play();
            RequestPlay?.Invoke(this, EventArgs.Empty);
        }

        public virtual void ForcePlay() {
            Play();
        }

        public virtual void Pause() {
            MediaState = MediaState.Pause;
            Player.Pause();
            RequestPause?.Invoke(this, EventArgs.Empty);
        }

        public void Stop() {
            // Force save position before stopping
            SaveCurrentPosition();
            Player.Stop();
            RequestStop?.Invoke(this, EventArgs.Empty);
        }

        public virtual void SyncPosition(TimeSpan position) {
            Player.Seek((int)position.TotalMilliseconds);
            RequestSyncPosition?.Invoke(this, position);
        }

        public (int index, long positionTicks, double speed, string[] paths) GetPlaybackState() {
            // Return current state for persistence
            // Note: _files might be large, but we only need paths
            var paths = _files?.Select(f => f.FilePath).ToArray() ?? Array.Empty<string>();
            var pos = LastPositionRecord.timestamp > 0 ? LastPositionRecord.timestamp : 0; 
            // Actually LastPositionRecord.position is the TimeSpan position.
            return (_currentPos, LastPositionRecord.position.Ticks, _speedRatio, paths);
        }

        public void RestoreState(int index, long positionTicks) {
            if (_files == null || _files.Length == 0) return;
            
            // Validate index
            if (index >= 0 && index < _files.Length) {
                _currentPos = index;
                // We need to signal that we want to start at this position
                // Typically Play(index) would be called.
                // But we want to seek too.
                // We can set a "PendingSeek" or just rely on the fact that LoadCurrentVideo hasn't happened yet?
                // If the window is just shown, LoadCurrentVideo might be called soon.
                
                // Let's set the index and let LoadCurrentVideo handle the loading.
                // But we need to SEEK after loading.
                
                // We'll use a specific method or modify LoadCurrentVideo
                // Ideally, we can set a property that OnMediaOpened uses to Seek.
                _pendingSeekPosition = TimeSpan.FromTicks(positionTicks);
                _ = LoadCurrentVideo();
            }
        }
        
        private TimeSpan? _pendingSeekPosition;
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint GetShortPathName(string lpszLongPath, System.Text.StringBuilder lpszShortPath, uint cchBuffer);

        private string GetShortPath(string path) {
            try {
                // Return original if path is invalid or too short to need conversion
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return path;

                var sb = new System.Text.StringBuilder(255);
                uint result = GetShortPathName(path, sb, (uint)sb.Capacity);
                
                // If buffer is too small, resize and retry
                if (result > sb.Capacity) {
                    sb = new System.Text.StringBuilder((int)result);
                    result = GetShortPathName(path, sb, (uint)sb.Capacity);
                }
                
                if (result > 0) return sb.ToString();
            } catch (Exception ex) {
                Logger.Warning($"Failed to get short path for {path}", ex);
            }
            return path;
        }

        private bool _disposed = false;
        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            
            Logger.Info($"[HypnoViewModel] Disposing (Current: {CurrentSource})");
            
            // Force save position before disposing
            SaveCurrentPosition();
            
            try {
                if (Player != null) {
                    Player.OpenCompleted  -= Player_OpenCompleted;
                    Player.PropertyChanged -= Player_PropertyChanged;
                    
                    Player.Dispose();
                    Logger.Info("[HypnoViewModel] Player disposed successfully");
                }
            } catch (Exception ex) {
                Logger.Error("Error disposing Player in HypnoViewModel", ex);
            }
            
            _preBufferCts?.Cancel();
            _preBufferCts?.Dispose();
            _loadCts?.Cancel();
            _loadCts?.Dispose();
        }
    }

    /// <summary>
    /// Event arguments for media error events
    /// </summary>
    public class MediaErrorEventArgs : EventArgs {
        public string ErrorMessage { get; }
        
        public MediaErrorEventArgs(string errorMessage) {
            ErrorMessage = errorMessage;
        }

    }
}
