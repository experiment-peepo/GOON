using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using TrainMeX.Classes;

namespace TrainMeX.ViewModels {
    public class HypnoViewModel : ObservableObject {
        private VideoItem[] _files;
        private int _currentPos = 0;
        private int _consecutiveFailures = 0;
        private const int MaxConsecutiveFailures = 10; // Stop retrying after 10 consecutive failures
        private Dictionary<string, int> _fileFailureCounts = new Dictionary<string, int>(); // Track failures per file
        private const int MaxFailuresPerFile = 3; // Skip a file after 3 failures
        private bool _isLoading = false; // Prevent concurrent LoadCurrentVideo() calls
        private readonly object _loadLock = new object(); // Lock for loading operations
        private Uri _expectedSource = null; // Track the source we're expecting MediaOpened for
        
        private Uri _currentSource;
        public Uri CurrentSource {
            get => _currentSource;
            set {
                SetProperty(ref _currentSource, value);
            }
        }

        private double _opacity;
        public double Opacity {
            get => _opacity;
            set => SetProperty(ref _opacity, value);
        }

        private double _volume;
        public double Volume {
            get => _volume;
            set => SetProperty(ref _volume, value);
        }

        private MediaState _mediaState = MediaState.Manual;
        public MediaState MediaState {
            get => _mediaState;
            set => SetProperty(ref _mediaState, value);
        }
        
        public event EventHandler RequestPlay;
        public event EventHandler RequestPause;
        public event EventHandler RequestStop;
        public event EventHandler RequestStopBeforeSourceChange;
        public event EventHandler<MediaErrorEventArgs> MediaErrorOccurred;

        public HypnoViewModel() {
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
            }
            
            // Materialize to array for indexed access - this is necessary for PlayNext() logic
            _files = files?.ToArray() ?? Array.Empty<VideoItem>();
            _currentPos = -1;
            _consecutiveFailures = 0; // Reset failure counter when queue changes
            _fileFailureCounts.Clear(); // Clear per-file failure counts when queue changes
            
            // Clear expected source to stop any in-progress loading (must be in lock for thread safety)
            lock (_loadLock) {
                _expectedSource = null;
            }
            
            // Clear current source to stop any in-progress loading
            // This prevents MediaOpened events from old sources firing after queue change
            CurrentSource = null;
            
            // Start playing the new queue
            PlayNext();
        }

        private VideoItem _currentItem;

        public void PlayNext() {
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
                if (_currentPos + 1 < _files.Length) {
                    _currentPos++;
                } else {
                    _currentPos = 0; // Loop
                }
                
                attempts++;
                
                // Prevent infinite loop if all files have failed
                if (attempts > _files.Length) {
                    Logger.Warning("All videos in queue have failed too many times. Stopping playback.");
                    MediaErrorOccurred?.Invoke(this, new MediaErrorEventArgs("All videos in queue have failed. Please check your video files."));
                    return;
                }
                
                // Check if current file has failed too many times
                var currentPath = _files[_currentPos]?.FilePath;
                if (currentPath != null && _fileFailureCounts.TryGetValue(currentPath, out int failures) && failures >= MaxFailuresPerFile) {
                    continue; // Skip this file, try next
                }
                
                break; // Found a valid file
            } while (true);

            LoadCurrentVideo();
        }

        private void LoadCurrentVideo() {
            // Prevent concurrent calls to LoadCurrentVideo
            lock (_loadLock) {
                if (_isLoading) {
                    Logger.Warning("LoadCurrentVideo() called while already loading, skipping");
                    return;
                }
                _isLoading = true;
            }

            try {
                if (_currentItem != null) {
                    _currentItem.PropertyChanged -= CurrentItem_PropertyChanged;
                }

                if (_files == null || _files.Length == 0 || _currentPos < 0 || _currentPos >= _files.Length) {
                    lock (_loadLock) {
                        _isLoading = false;
                    }
                    return;
                }

                _currentItem = _files[_currentPos];
                _currentItem.PropertyChanged += CurrentItem_PropertyChanged;
                
                var path = _currentItem.FilePath;
                
                // Validate based on whether it's a URL or local file
                if (_currentItem.IsUrl) {
                    // For URLs, validate URL format
                    if (!FileValidator.ValidateVideoUrl(path, out string urlValidationError)) {
                        Logger.Warning($"URL validation failed for '{_currentItem.FileName}': {urlValidationError}. Skipping to next video.");
                        lock (_loadLock) {
                            _isLoading = false;
                        }
                        PlayNext();
                        return;
                    }
                } else {
                    // For local files, check if path is rooted
                    if (!Path.IsPathRooted(path)) {
                        lock (_loadLock) {
                            _isLoading = false;
                        }
                        return;
                    }
                    
                    // Re-validate file existence before attempting to load
                    // Files could be deleted or become inaccessible between queue setup and playback
                    if (!FileValidator.ValidateVideoFile(path, out string validationError)) {
                        Logger.Warning($"File validation failed for '{_currentItem.FileName}': {validationError}. Skipping to next video.");
                        // Reset loading flag before calling PlayNext() recursively
                        lock (_loadLock) {
                            _isLoading = false;
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
                    // For local files, use absolute file URI
                    newSource = new Uri(path, UriKind.Absolute);
                }
                
                // Set expected source inside lock for thread safety
                lock (_loadLock) {
                    _expectedSource = newSource;
                }
                
                // Set the source - MediaOpened event will trigger playback
                CurrentSource = newSource;
                
                // Don't call RequestPlay here - wait for MediaOpened to confirm successful load
                // This prevents timing issues where Play() is called before MediaElement is ready
            } catch (Exception ex) {
                Logger.Error("Error in LoadCurrentVideo()", ex);
                // Reset loading flag before calling PlayNext() recursively
                lock (_loadLock) {
                    _isLoading = false;
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
            _consecutiveFailures = 0; // Reset failure counter on successful playback
            
            // Clear failure count for this file since it played successfully
            if (_currentItem?.FilePath != null && _fileFailureCounts.ContainsKey(_currentItem.FilePath)) {
                _fileFailureCounts.Remove(_currentItem.FilePath);
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
            }
            
            // Reset failure counter when video successfully opens
            _consecutiveFailures = 0;
            
            // Clear failure count for this file since it opened successfully
            if (_currentItem?.FilePath != null && _fileFailureCounts.ContainsKey(_currentItem.FilePath)) {
                _fileFailureCounts.Remove(_currentItem.FilePath);
            }
            
            // Request play now that media is confirmed loaded
            // This ensures Play() is only called after MediaElement has processed the source
            RequestPlay?.Invoke(this, EventArgs.Empty);
        }

        public void OnMediaFailed(Exception ex) {
            // Reset loading flag on failure
            // This must be done in a lock to ensure thread safety
            lock (_loadLock) {
                _isLoading = false;
            }
            
            var fileName = _currentItem?.FileName ?? "Unknown";
            var filePath = _currentItem?.FilePath;
            var errorMessage = $"Failed to play video: {fileName}";
            
            Logger.Error(errorMessage, ex);
            
            // Increment failure counters
            _consecutiveFailures++;
            
            // Track failures per file
            if (filePath != null) {
                if (!_fileFailureCounts.ContainsKey(filePath)) {
                    _fileFailureCounts[filePath] = 0;
                }
                _fileFailureCounts[filePath]++;
                
                Logger.Warning($"File '{fileName}' has failed {_fileFailureCounts[filePath]} time(s). Will skip after {MaxFailuresPerFile} failures.");
            }
            
            // Notify listeners (e.g., UI) about the error
            MediaErrorOccurred?.Invoke(this, new MediaErrorEventArgs($"{errorMessage}. Error: {ex?.Message ?? "Unknown error"}"));
            
            // Stop retrying if we've exceeded the failure threshold
            // This prevents infinite retry loops when all videos fail
            if (_consecutiveFailures >= MaxConsecutiveFailures) {
                Logger.Warning($"Stopped retrying after {MaxConsecutiveFailures} consecutive failures. All videos in queue may be invalid.");
                MediaErrorOccurred?.Invoke(this, new MediaErrorEventArgs($"Playback stopped after {MaxConsecutiveFailures} consecutive failures. Please check your video files."));
                return;
            }
            
            // Skip to next video to avoid getting stuck
            PlayNext();
        }

        public void Play() {
            RequestPlay?.Invoke(this, EventArgs.Empty);
        }

        public void Pause() {
            RequestPause?.Invoke(this, EventArgs.Empty);
        }

        public void Stop() {
            RequestStop?.Invoke(this, EventArgs.Empty);
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
