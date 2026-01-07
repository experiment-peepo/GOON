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

namespace GOON.ViewModels {
    public class HypnoViewModel : ObservableObject {
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
        
        private (TimeSpan position, long timestamp) _lastPositionRecord;
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
            set => SetProperty(ref _opacity, value);
        }

        public void RefreshOpacity() {
            OnPropertyChanged(nameof(Opacity));
        }

        private double _volume;
        public virtual double Volume {
            get => _volume;
            set {
                if (SetProperty(ref _volume, value)) {
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
            set => SetProperty(ref _speedRatio, value);
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

        public HypnoViewModel() {
            SkipCommand = new RelayCommand(_ => PlayNext());
            TogglePlayPauseCommand = new RelayCommand(_ => TogglePlayPause());
        }

        public virtual void TogglePlayPause() {
            if (MediaState == MediaState.Play) {
                Pause();
            } else {
                Play();
            }
        }

        public void SetQueue(IEnumerable<VideoItem> files) {
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
            
            // Start playing the new queue
            PlayNext();
        }

        private VideoItem _currentItem;

        public virtual void PlayNext() {
            if (_files == null || _files.Length == 0) return;

            // Prevent rapid/concurrent calls to PlayNext() while loading
            // This protects against race conditions when PlayNext() is called multiple times quickly
            lock (_loadLock) {
                if (_isLoading) {
                    Logger.Warning("PlayNext() called while already loading, skipping to prevent race condition");
                    return;
                }
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

            LoadCurrentVideo();
            Logger.Info($"Next video: #{_currentPos} - {_currentItem?.FileName ?? "Unknown"}");
        }

        private void LoadCurrentVideo() {
            // Prevent concurrent calls to LoadCurrentVideo
            lock (_loadLock) {
                if (_isLoading) {
                    Logger.Warning("LoadCurrentVideo() called while already loading, skipping");
                    return;
                }
                _isLoading = true; // Set flag inside lock to prevent race condition
                
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
                // These URLs have the page URL concatenated with the video URL (e.g., "https://rule34video.com/video/.../function/0/https://...")
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
                        PlayNext();
                        return;
                    }
                    Logger.Info($"LoadCurrentVideo: URL validation passed for: {path}");
                } else {
                    // For local files, check if path is rooted
                    if (!Path.IsPathRooted(path)) {
                        Logger.Warning($"Non-rooted path detected for '{_currentItem.FileName}': {path}. Skipping to next video.");
                        // Skip to next video instead of stalling
                        PlayNext();
                        return;
                    }
                    
                    // Re-validate file existence before attempting to load
                    // Files could be deleted or become inaccessible between queue setup and playback
                    if (!FileValidator.ValidateVideoFile(path, out string validationError)) {
                        Logger.Warning($"File validation failed for '{_currentItem.FileName}': {validationError}. Skipping to next video.");
                        // Reset loading flag before calling PlayNext() recursively
                        lock (_loadLock) {
                            _isLoading = false;
                            _recursionDepth = Math.Max(0, _recursionDepth - 1); // Decrement before recursive call
                        }
                        // Skip to next video instead of failing
                        // PlayNext() will check loading state and proceed safely
                        PlayNext();
                        return;
                    }
                }
                
                // Apply per-monitor/per-item settings
                Opacity = _currentItem.Opacity;
                Volume = _currentItem.Volume;
                
                // Stop the current video before changing source to ensure MediaEnded fires reliably
                // This fixes an issue where MediaEnded doesn't fire on secondary monitors
                RequestStopBeforeSourceChange?.Invoke(this, EventArgs.Empty);
                
                // Set the expected source before changing CurrentSource
                // This allows OnMediaOpened to verify the opened media matches what we expect
                // Handle both local files and URLs
                Uri newSource;
                if (_currentItem.IsUrl) {
                    // For URLs, use the URL directly
                    newSource = new Uri(path, UriKind.Absolute);
                } else {
                // Verify file existence for local paths
                if (Path.IsPathRooted(path) && !path.StartsWith("http")) {
                   if (!File.Exists(path)) {
                       Logger.Error($"LoadCurrentVideo: File not found at path: {path}");
                   } else {
                       Logger.Info($"LoadCurrentVideo: File verified to exist: {path}");
                       
                       // CRITICAL FIX: MediaElement (via WMP) has issues with special chars like brackets [] and sometimes spaces or long paths in URIs.
                       // Convert to 8.3 short path (e.g. PROJECT~1.MOV) to bypass this completely.
                       // We check for brackets, spaces, and non-ASCII characters.
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
                
                // Set expected source inside lock for thread safety
                lock (_loadLock) {
                    _expectedSource = newSource;
                }
                
                // CRITICAL FIX for single-video looping:
                // If the new source is the same as current source, MediaElement won't fire MediaOpened
                // To force reload, set source to null first
                if (CurrentSource != null && CurrentSource.Equals(newSource)) {
                    CurrentSource = null; // Clear source to force reload
                }
                
                // Set the source - MediaOpened event will trigger playback
                try {
                     CurrentSource = newSource;
                } catch (Exception ex) {
                     Logger.Error($"LoadCurrentVideo: Error setting CurrentSource to '{newSource}': {ex.Message}");
                     OnMediaFailed(ex);
                }                // This prevents timing issues where Play() is called before MediaElement is ready
            } catch (Exception ex) {
                Logger.Error("Error in LoadCurrentVideo()", ex);
                // Reset loading flag before calling PlayNext() recursively
                lock (_loadLock) {
                    _isLoading = false;
                    _recursionDepth = Math.Max(0, _recursionDepth - 1); // Decrement before recursive call
                }
                // Try next video on error
                // PlayNext() will check loading state and proceed safely
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

        public void OnMediaEnded() {
            Logger.Info($"Media ended: {_currentItem?.FileName ?? "Unknown"}");
            _consecutiveFailures = 0; // Reset failure counter on successful playback
            
            // Clear failure count for this file since it played successfully (thread-safe)
            if (_currentItem?.FilePath != null) {
                _fileFailureCounts.TryRemove(_currentItem.FilePath, out _);
            }
            
            // Reset recursion depth on successful completion
            lock (_loadLock) {
                _recursionDepth = 0;
            }
            
            PlayNext();
        }
        
        public void OnMediaOpened() {
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
            // Clear failure count for this file since it opened successfully (thread-safe)
            if (_currentItem?.FilePath != null) {
                _fileFailureCounts.TryRemove(_currentItem.FilePath, out _);
            }
            
            if (UseCoordinatedStart) {
        // Coordinated start: Pause, signal Ready, and wait.
        // We avoid an explicit SyncPosition(Zero) here to allow the resume logic 
        // in HypnoWindow to set the correct starting position if available.
        MediaState = MediaState.Pause;
        IsReady = true;
        RequestReady?.Invoke(this, EventArgs.Empty);
    } else {
                // Request play now that media is confirmed loaded
                // This ensures Play() is only called after MediaElement has processed the source
                Play();
            }
        }

        public void OnMediaFailed(Exception ex) {
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
            
            // Skip to next video to avoid getting stuck
            // Add a delay to allow GPU resources to free up (especially for 0x8898050C errors)
            int delayMs = isCodecError ? 500 : 300;
            _ = Task.Delay(delayMs).ContinueWith(_ => {
                Application.Current?.Dispatcher.InvokeAsync(() => PlayNext());
            });
        }

        public virtual void Play() {
            MediaState = MediaState.Play;
            IsReady = false; // No longer just "Ready", actually playing
            RequestPlay?.Invoke(this, EventArgs.Empty);
        }

        public virtual void ForcePlay() {
            Play();
        }

        public virtual void Pause() {
            MediaState = MediaState.Pause;
            RequestPause?.Invoke(this, EventArgs.Empty);
        }

        public void Stop() {
            RequestStop?.Invoke(this, EventArgs.Empty);
        }

        public void SyncPosition(TimeSpan position) {
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
                LoadCurrentVideo();
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
