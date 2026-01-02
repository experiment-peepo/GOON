using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TrainMeX.Classes;
using TrainMeX.Windows;
using Microsoft.Win32;

namespace TrainMeX.ViewModels {
    /// <summary>
    /// Type of status message for styling purposes
    /// </summary>
    public enum StatusMessageType {
        Info,
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// ViewModel for the main launcher window, managing video files, screens, and playback
    /// </summary>
    public class LauncherViewModel : ObservableObject, IDisposable {
        public ObservableCollection<VideoItem> AddedFiles { get; } = new ObservableCollection<VideoItem>();
        public ObservableCollection<ScreenViewer> AvailableScreens { get; } = new ObservableCollection<ScreenViewer>();
        
        private Random random = new Random();

        private bool _shuffle;
        public bool Shuffle {
            get => _shuffle;
            set => SetProperty(ref _shuffle, value);
        }

        private string _hypnotizeButtonText = "TRAIN ME!";
        public string HypnotizeButtonText {
            get => _hypnotizeButtonText;
            set => SetProperty(ref _hypnotizeButtonText, value);
        }

        private bool _isHypnotizeEnabled;
        public bool IsHypnotizeEnabled {
            get => _isHypnotizeEnabled;
            set => SetProperty(ref _isHypnotizeEnabled, value);
        }

        private bool _isDehypnotizeEnabled;
        public bool IsDehypnotizeEnabled {
            get => _isDehypnotizeEnabled;
            set => SetProperty(ref _isDehypnotizeEnabled, value);
        }

        private bool _isPauseEnabled;
        public bool IsPauseEnabled {
            get => _isPauseEnabled;
            set => SetProperty(ref _isPauseEnabled, value);
        }

        private string _pauseButtonText = "Pause";
        public string PauseButtonText {
            get => _pauseButtonText;
            set => SetProperty(ref _pauseButtonText, value);
        }

        private bool _pauseClicked;
        private bool _allFilesAssigned = false;
        private string _statusMessage;
        private StatusMessageType _statusMessageType = StatusMessageType.Info;
        private bool _isLoading;

        public string StatusMessage {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public StatusMessageType StatusMessageType {
            get => _statusMessageType;
            set => SetProperty(ref _statusMessageType, value);
        }

        /// <summary>
        /// Helper method to set status message with type
        /// </summary>
        private void SetStatusMessage(string message, StatusMessageType type = StatusMessageType.Info) {
            StatusMessage = message;
            StatusMessageType = type;
        }

        public bool IsLoading {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public ICommand HypnotizeCommand { get; }
        public ICommand DehypnotizeCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand BrowseCommand { get; }
        public ICommand AddUrlCommand { get; }
        public ICommand ImportPlaylistCommand { get; }
        public ICommand RemoveSelectedCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand MinimizeCommand { get; }

        private System.Windows.Threading.DispatcherTimer _saveTimer;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly VideoUrlExtractor _urlExtractor;
        private readonly PlaylistImporter _playlistImporter;

        public LauncherViewModel() {
            _cancellationTokenSource = new CancellationTokenSource();
            _urlExtractor = new VideoUrlExtractor();
            _playlistImporter = new PlaylistImporter(_urlExtractor);
            RefreshScreens();

            HypnotizeCommand = new RelayCommand(Hypnotize, _ => IsHypnotizeEnabled);
            DehypnotizeCommand = new RelayCommand(Dehypnotize);
            PauseCommand = new RelayCommand(Pause);
            BrowseCommand = new RelayCommand(Browse);
            AddUrlCommand = new RelayCommand(AddUrl);
            ImportPlaylistCommand = new RelayCommand(ImportPlaylist);
            RemoveSelectedCommand = new RelayCommand(RemoveSelected);
            RemoveItemCommand = new RelayCommand(RemoveItem);
            ClearAllCommand = new RelayCommand(ClearAll);
            SavePlaylistCommand = new RelayCommand(SavePlaylist);
            LoadPlaylistCommand = new RelayCommand(LoadPlaylist);
            ExitCommand = new RelayCommand(Exit);
            MinimizeCommand = new RelayCommand(Minimize);

            // Subscribe to media error events
            App.VideoService.MediaErrorOccurred += VideoService_MediaErrorOccurred;

            UpdateButtons();
            
            // Load session if auto-load is enabled (async to avoid blocking UI)
            if (App.Settings.AutoLoadSession) {
                _ = LoadSessionAsync(_cancellationTokenSource.Token);
            }
            
            // Subscribe to display settings changes to invalidate screen cache
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            
            _saveTimer = new System.Windows.Threading.DispatcherTimer();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(500);
            _saveTimer.Tick += (s, e) => {
                _saveTimer.Stop();
                SaveSession();
            };
            
            AddedFiles.CollectionChanged += (s, e) => {
                if (e.NewItems != null) {
                    foreach (VideoItem item in e.NewItems) {
                        item.PropertyChanged += VideoItem_PropertyChanged;
                        // Track assignment status incrementally
                        if (item.AssignedScreen == null) _allFilesAssigned = false;
                    }
                }
                if (e.OldItems != null) {
                    foreach (VideoItem item in e.OldItems) item.PropertyChanged -= VideoItem_PropertyChanged;
                }
                // Recalculate assignment status when collection changes
                UpdateAllFilesAssigned();
            };
            foreach (var item in AddedFiles) item.PropertyChanged += VideoItem_PropertyChanged;
            UpdateAllFilesAssigned();
        }

        [SupportedOSPlatform("windows")]
        private void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e) {
            // Invalidate screen cache when display settings change
            InvalidateScreenCache();
            // Refresh screens on UI thread
            Application.Current?.Dispatcher.InvokeAsync(() => {
                RefreshScreens();
            });
        }

        private void VideoItem_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(VideoItem.Opacity) || e.PropertyName == nameof(VideoItem.Volume) || e.PropertyName == nameof(VideoItem.AssignedScreen)) {
                TriggerDebouncedSave();
                // Update assignment status when AssignedScreen changes
                if (e.PropertyName == nameof(VideoItem.AssignedScreen)) {
                    UpdateAllFilesAssigned();
                }
            }
        }

        private void TriggerDebouncedSave() {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        public ICommand RemoveItemCommand { get; }
        public ICommand SavePlaylistCommand { get; }
        public ICommand LoadPlaylistCommand { get; }

        private void RemoveItem(object parameter) {
            if (parameter is VideoItem item) {
                AddedFiles.Remove(item);
                UpdateButtons();
                SaveSession();
            }
        }

        private List<ScreenViewer> _cachedScreens;
        private DateTime _screensCacheTime = DateTime.MinValue;
        private static readonly TimeSpan ScreenCacheTimeout = TimeSpan.FromSeconds(5);

        [SupportedOSPlatform("windows")]
        private void RefreshScreens() {
            // Use cached screens if available and recent
            if (_cachedScreens != null && DateTime.Now - _screensCacheTime < ScreenCacheTimeout) {
                AvailableScreens.Clear();
                foreach (var s in _cachedScreens) {
                    AvailableScreens.Add(s);
                }
                return;
            }

            try {
                var screens = WindowServices.GetAllScreenViewers();
                _cachedScreens = screens;
                _screensCacheTime = DateTime.Now;
                
                AvailableScreens.Clear();
                foreach (var s in screens) {
                    AvailableScreens.Add(s);
                }
                
                // Ensure we have at least one screen
                if (AvailableScreens.Count == 0) {
                    Logger.Warning("No screens detected, using default screen");
                    SetStatusMessage("Warning: No screens detected. Using default screen.", StatusMessageType.Warning);
                    // Add a default screen
                    var defaultScreen = System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens.FirstOrDefault();
                    if (defaultScreen != null) {
                        var defaultViewer = new ScreenViewer(defaultScreen);
                        AvailableScreens.Add(defaultViewer);
                        _cachedScreens = new List<ScreenViewer> { defaultViewer };
                    }
                }
            } catch (Exception ex) {
                Logger.Error("Error refreshing screens", ex);
                SetStatusMessage($"Error refreshing screens: {ex.Message}", StatusMessageType.Error);
                // Add a fallback screen
                try {
                    var fallbackScreen = System.Windows.Forms.Screen.PrimaryScreen;
                    if (fallbackScreen != null) {
                        var fallbackViewer = new ScreenViewer(fallbackScreen);
                        AvailableScreens.Add(fallbackViewer);
                        _cachedScreens = new List<ScreenViewer> { fallbackViewer };
                    }
                } catch (Exception ex2) {
                    Logger.Error("Failed to add fallback screen", ex2);
                }
            }
        }

        public void InvalidateScreenCache() {
            _cachedScreens = null;
            _screensCacheTime = DateTime.MinValue;
        }

        /// <summary>
        /// Gets the default screen based on settings, falling back to primary screen if the saved monitor is unavailable
        /// </summary>
        [SupportedOSPlatform("windows")]
        private ScreenViewer GetDefaultScreen() {
            if (AvailableScreens.Count == 0) RefreshScreens();
            
            var settings = App.Settings;
            if (!string.IsNullOrEmpty(settings.DefaultMonitorDeviceName)) {
                var defaultScreen = AvailableScreens.FirstOrDefault(s => s.DeviceName == settings.DefaultMonitorDeviceName);
                if (defaultScreen != null) {
                    return defaultScreen;
                }
            }
            
            // Fall back to primary screen, or first available if no primary
            return AvailableScreens.FirstOrDefault(v => v.Screen.Primary) ?? AvailableScreens.FirstOrDefault();
        }

        private void UpdateButtons() {
            bool hasFiles = AddedFiles.Count > 0;
            IsHypnotizeEnabled = hasFiles && _allFilesAssigned;
        }

        private void UpdateAllFilesAssigned() {
            _allFilesAssigned = AddedFiles.Count > 0 && AddedFiles.All(f => f.AssignedScreen != null);
            UpdateButtons();
        }

        private void Hypnotize(object parameter) {
            var selectedItems = parameter as System.Collections.IList;
            var assignments = BuildAssignmentsFromSelection(selectedItems);
            if (assignments == null || assignments.Count == 0) return;
            
            // Use async version to avoid deadlocks
            _ = PlayPerMonitorAsync(assignments).ContinueWith(task => {
                if (task.IsFaulted) {
                    var ex = task.Exception?.GetBaseException() ?? task.Exception;
                    Logger.Error("Error starting playback", ex);
                    Application.Current?.Dispatcher.InvokeAsync(() => {
                        SetStatusMessage($"Error starting playback: {ex?.Message ?? "Unknown error"}", StatusMessageType.Error);
                    });
                } else {
                    Application.Current?.Dispatcher.InvokeAsync(() => {
                        IsDehypnotizeEnabled = true;
                        IsPauseEnabled = true;
                    });
                }
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private async Task PlayPerMonitorAsync(IDictionary<ScreenViewer, IEnumerable<VideoItem>> assignments) {
            await App.VideoService.PlayPerMonitorAsync(assignments).ConfigureAwait(false);
        }

        private Dictionary<ScreenViewer, IEnumerable<VideoItem>> BuildAssignmentsFromSelection(System.Collections.IList selectedItems) {
            var selectedFiles = new List<VideoItem>();
            if (selectedItems != null && selectedItems.Count > 0) {
                foreach (VideoItem f in selectedItems) selectedFiles.Add(f);
            } else {
                foreach (var f in AddedFiles) selectedFiles.Add(f);
            }

            if (selectedFiles.Count < 1) return null;

            // Simple validation again just in case
            if (selectedFiles.Any(x => x.AssignedScreen == null)) return null;

            if (Shuffle) {
                // Fisher-Yates shuffle - O(n) instead of O(n log n)
                for (int i = selectedFiles.Count - 1; i > 0; i--) {
                    int j = random.Next(i + 1);
                    var temp = selectedFiles[i];
                    selectedFiles[i] = selectedFiles[j];
                    selectedFiles[j] = temp;
                }
            }

            var assignments = new Dictionary<ScreenViewer, IEnumerable<VideoItem>>();
            foreach (var f in selectedFiles) {
                var assigned = f.AssignedScreen;
                if (assigned == null) continue;
                if (!assignments.ContainsKey(assigned)) assignments[assigned] = new List<VideoItem>();
                ((List<VideoItem>)assignments[assigned]).Add(f);
            }
            return assignments;
        }

        private void Dehypnotize(object obj) {
            IsDehypnotizeEnabled = false;
            IsPauseEnabled = false;
            App.VideoService.StopAll();
        }

        private void Pause(object obj) {
            if (_pauseClicked) {
                _pauseClicked = false;
                PauseButtonText = "Pause";
                App.VideoService.ContinueAll();
            } else {
                _pauseClicked = true;
                PauseButtonText = "Continue";
                App.VideoService.PauseAll();
            }
        }

        private void Browse(object obj) {
            // Safely execute async code from void command handler
            // This pattern ensures exceptions are properly caught and handled
            _ = BrowseAsync().ContinueWith(task => {
                if (task.IsFaulted) {
                    var ex = task.Exception?.GetBaseException() ?? task.Exception;
                    Logger.Error("Error in Browse operation", ex);
                    Application.Current?.Dispatcher.InvokeAsync(() => {
                        SetStatusMessage($"Error browsing files: {ex?.Message ?? "Unknown error"}", StatusMessageType.Error);
                    });
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private async Task BrowseAsync() {
            var dlg = new OpenFileDialog {
                Multiselect = true,
                Filter = $"Video Files|{string.Join(";", Constants.VideoExtensions.Select(e => $"*{e}"))}|All Files|*.*"
            };
            if (dlg.ShowDialog() == true) {
                await AddFilesAsync(dlg.FileNames, _cancellationTokenSource.Token);
            }
        }

        private void AddUrl(object obj) {
            _ = AddUrlAsync().ContinueWith(task => {
                if (task.IsFaulted) {
                    var ex = task.Exception?.GetBaseException() ?? task.Exception;
                    Logger.Error("Error in AddUrl operation", ex);
                    Application.Current?.Dispatcher.InvokeAsync(() => {
                        SetStatusMessage($"Error adding URL: {ex?.Message ?? "Unknown error"}", StatusMessageType.Error);
                    });
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private async Task AddUrlAsync() {
            IsLoading = true;
            SetStatusMessage("Enter video URL...", StatusMessageType.Info);

            try {
                // Show input dialog
                var inputUrl = InputDialogWindow.ShowDialog(
                    Application.Current.MainWindow, 
                    "Add Video URL", 
                    "Video URL:"
                );
                if (string.IsNullOrWhiteSpace(inputUrl)) {
                    IsLoading = false;
                    return;
                }
                await ProcessUrlAsync(inputUrl.Trim());
            } catch (Exception ex) {
                Logger.Error("Error in AddUrlAsync", ex);
                SetStatusMessage($"Error adding URL: {ex.Message}", StatusMessageType.Error);
            } finally {
                IsLoading = false;
            }
        }

        private async Task ProcessUrlAsync(string inputUrl) {
            SetStatusMessage("Processing URL...", StatusMessageType.Info);

            try {
                // Check if it's a page URL that needs extraction
                string finalUrl = inputUrl;
                if (FileValidator.IsPageUrl(inputUrl)) {
                    SetStatusMessage("Extracting video URL from page...", StatusMessageType.Info);
                    finalUrl = await _urlExtractor.ExtractVideoUrlAsync(inputUrl, _cancellationTokenSource.Token);
                    
                    if (string.IsNullOrWhiteSpace(finalUrl)) {
                        SetStatusMessage("Failed to extract video URL from page. The page may not contain a video or the site structure may have changed.", StatusMessageType.Error);
                        return;
                    }
                }

                // Validate the URL
                if (!FileValidator.ValidateVideoUrl(finalUrl, out string errorMessage)) {
                    SetStatusMessage($"Invalid video URL: {errorMessage}", StatusMessageType.Error);
                    return;
                }

                // Check for duplicates
                var normalizedUrl = FileValidator.NormalizeUrl(finalUrl);
                var existingPaths = new HashSet<string>(AddedFiles.Select(x => 
                    x.IsUrl ? FileValidator.NormalizeUrl(x.FilePath) : x.FilePath), 
                    StringComparer.OrdinalIgnoreCase);

                if (existingPaths.Contains(normalizedUrl)) {
                    SetStatusMessage("URL is already in the playlist", StatusMessageType.Warning);
                    return;
                }

                // Ensure screens are up to date
                if (AvailableScreens.Count == 0) RefreshScreens();
                var defaultScreen = GetDefaultScreen();

                // Create and add video item
                var item = new VideoItem(finalUrl, defaultScreen);
                var settings = App.Settings;
                item.Opacity = settings.DefaultOpacity;
                item.Volume = settings.DefaultVolume;
                
                // Try to extract title if it was a page URL (but never fail if extraction fails)
                if (FileValidator.IsPageUrl(inputUrl)) {
                    try {
                        var title = await _urlExtractor.ExtractVideoTitleAsync(inputUrl, _cancellationTokenSource.Token);
                        if (!string.IsNullOrWhiteSpace(title)) {
                            item.Title = title;
                        }
                    } catch (Exception ex) {
                        Logger.Warning($"Error extracting title from {inputUrl}: {ex.Message}. VideoItem created without title.");
                        // Continue - VideoItem will use URL-based name extraction
                    }
                }
                
                item.Validate();

                if (item.ValidationStatus == FileValidationStatus.Valid) {
                    AddedFiles.Add(item);
                    SetStatusMessage($"Added URL: {item.FileName}", StatusMessageType.Success);
                    UpdateButtons();
                    SaveSession();
                } else {
                    SetStatusMessage($"URL validation failed: {item.ValidationError}", StatusMessageType.Error);
                }
            } catch (OperationCanceledException) {
                SetStatusMessage("Operation cancelled", StatusMessageType.Warning);
            } catch (Exception ex) {
                Logger.Error("Error processing URL", ex);
                SetStatusMessage($"Error processing URL: {ex.Message}", StatusMessageType.Error);
            }
        }

        private void ImportPlaylist(object obj) {
            _ = ImportPlaylistAsync().ContinueWith(task => {
                if (task.IsFaulted) {
                    var ex = task.Exception?.GetBaseException() ?? task.Exception;
                    Logger.Error("Error in ImportPlaylist operation", ex);
                    Application.Current?.Dispatcher.InvokeAsync(() => {
                        SetStatusMessage($"Error importing playlist: {ex?.Message ?? "Unknown error"}", StatusMessageType.Error);
                    });
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private async Task ImportPlaylistAsync() {
            IsLoading = true;
            SetStatusMessage("Enter playlist URL...", StatusMessageType.Info);

            try {
                // Show input dialog
                var playlistUrl = InputDialogWindow.ShowDialog(
                    Application.Current.MainWindow, 
                    "Import Playlist", 
                    "Playlist URL:"
                );
                if (string.IsNullOrWhiteSpace(playlistUrl)) {
                    IsLoading = false;
                    return;
                }
                var trimmedUrl = playlistUrl.Trim();
                
                // Validate URL
                if (!FileValidator.IsValidUrl(trimmedUrl)) {
                    SetStatusMessage("Invalid playlist URL", StatusMessageType.Error);
                    IsLoading = false;
                    return;
                }

                SetStatusMessage("Importing playlist...", StatusMessageType.Info);

                // Import playlist with progress updates
                int current = 0;
                int total = 0;
                var videoItems = await _playlistImporter.ImportPlaylistAsync(
                    trimmedUrl,
                    (c, t) => {
                        current = c;
                        total = t;
                        Application.Current?.Dispatcher.InvokeAsync(() => {
                            SetStatusMessage($"Importing playlist... {c} of {t} videos", StatusMessageType.Info);
                        });
                    },
                    _cancellationTokenSource.Token
                );

                if (videoItems == null || videoItems.Count == 0) {
                    SetStatusMessage("No videos found in playlist", StatusMessageType.Warning);
                    IsLoading = false;
                    return;
                }

                // Ensure screens are up to date
                if (AvailableScreens.Count == 0) RefreshScreens();
                var defaultScreen = GetDefaultScreen();

                // Check for duplicates and add items
                var existingPaths = new HashSet<string>(AddedFiles.Select(x => 
                    x.IsUrl ? FileValidator.NormalizeUrl(x.FilePath) : x.FilePath), 
                    StringComparer.OrdinalIgnoreCase);

                var settings = App.Settings;
                int addedCount = 0;
                int skippedCount = 0;

                foreach (var item in videoItems) {
                    var normalizedUrl = item.IsUrl ? FileValidator.NormalizeUrl(item.FilePath) : item.FilePath;
                    if (existingPaths.Contains(normalizedUrl)) {
                        skippedCount++;
                        continue;
                    }

                    item.AssignedScreen = defaultScreen;
                    item.Opacity = settings.DefaultOpacity;
                    item.Volume = settings.DefaultVolume;
                    AddedFiles.Add(item);
                    existingPaths.Add(normalizedUrl);
                    addedCount++;
                }

                if (skippedCount > 0) {
                    SetStatusMessage($"Added {addedCount} video(s) from playlist, skipped {skippedCount} duplicate(s)", StatusMessageType.Success);
                } else {
                    SetStatusMessage($"Added {addedCount} video(s) from playlist", StatusMessageType.Success);
                }

                UpdateButtons();
                SaveSession();
            } catch (OperationCanceledException) {
                SetStatusMessage("Playlist import cancelled", StatusMessageType.Warning);
            } catch (Exception ex) {
                Logger.Error("Error importing playlist", ex);
                SetStatusMessage($"Error importing playlist: {ex.Message}", StatusMessageType.Error);
            } finally {
                IsLoading = false;
            }
        }

        private async System.Threading.Tasks.Task AddFilesAsync(string[] filePaths, CancellationToken cancellationToken = default) {
            IsLoading = true;
            SetStatusMessage("Validating files...", StatusMessageType.Info);
            
            try {
                cancellationToken.ThrowIfCancellationRequested();
                
                // Ensure screens are up to date
                if (AvailableScreens.Count == 0) RefreshScreens();
                var defaultScreen = GetDefaultScreen();
                
                // Use HashSet for O(1) lookups instead of O(n) Any() checks
                var existingPaths = new HashSet<string>(AddedFiles.Select(x => x.FilePath), StringComparer.OrdinalIgnoreCase);
                
                // Validate files in parallel
                var validationTasks = filePaths.Select(filePath => Task.Run(() => {
                    cancellationToken.ThrowIfCancellationRequested();
                if (existingPaths.Contains(filePath)) {
                    return (filePath, isValid: false, errorMessage: "File already in playlist");
                }
                
                if (!FileValidator.ValidateVideoFile(filePath, out string errorMessage)) {
                    return (filePath, isValid: false, errorMessage);
                }
                
                // Check file size and warn if large (no limit enforced)
                if (FileValidator.ValidateFileSize(filePath, out long size, out bool warning)) {
                    if (warning) {
                        Logger.Info($"Large file detected: {System.IO.Path.GetFileName(filePath)} ({size / (1024.0 * 1024 * 1024):F2} GB)");
                    }
                }
                return (filePath, isValid: true, errorMessage: (string)null);
                }, cancellationToken)).ToArray();
            
                cancellationToken.ThrowIfCancellationRequested();
            var results = await System.Threading.Tasks.Task.WhenAll(validationTasks);
            
                cancellationToken.ThrowIfCancellationRequested();
            var settings = App.Settings;
            int addedCount = 0;
            int skippedCount = 0;
            
            var failedFiles = new List<(string name, string error)>();
            
            foreach (var (filePath, isValid, errorMessage) in results) {
                cancellationToken.ThrowIfCancellationRequested();
                if (isValid) {
                    var sanitizedPath = FileValidator.SanitizePath(filePath);
                    if (sanitizedPath != null) {
                        var item = new VideoItem(sanitizedPath, defaultScreen);
                        item.Opacity = settings.DefaultOpacity;
                        item.Volume = settings.DefaultVolume;
                        // Validate the file to set its validation status
                        item.Validate();
                        AddedFiles.Add(item);
                        existingPaths.Add(sanitizedPath);
                        addedCount++;
                    } else {
                        Logger.Warning($"Failed to sanitize path: {filePath}");
                        failedFiles.Add((System.IO.Path.GetFileName(filePath), "Invalid file path"));
                        skippedCount++;
                    }
                } else {
                    Logger.Warning($"Skipped file {filePath}: {errorMessage}");
                    failedFiles.Add((System.IO.Path.GetFileName(filePath), errorMessage));
                    skippedCount++;
                }
            }
            
            if (skippedCount > 0) {
                var message = $"Added {addedCount} file(s), skipped {skippedCount} invalid file(s)";
                if (failedFiles.Count <= 5) {
                    message += ":\n" + string.Join("\n", failedFiles.Select(f => $"• {f.name} - {f.error}"));
                } else {
                    message += ":\n" + string.Join("\n", failedFiles.Take(5).Select(f => $"• {f.name} - {f.error}")) + $"\n... and {failedFiles.Count - 5} more";
                }
                SetStatusMessage(message, StatusMessageType.Warning);
            } else {
                SetStatusMessage($"Added {addedCount} file(s)", StatusMessageType.Success);
            }
            
                UpdateButtons();
                SaveSession();
            } catch (OperationCanceledException) {
                SetStatusMessage("Operation cancelled", StatusMessageType.Warning);
                Logger.Info("Add files operation was cancelled");
            } catch (Exception ex) {
                Logger.Error("Error adding files", ex);
                SetStatusMessage($"Error adding files: {ex.Message}", StatusMessageType.Error);
            } finally {
                IsLoading = false;
            }
        }
        
        private void RemoveSelected(object parameter) {
            var selectedItems = parameter as System.Collections.IList;
            if (selectedItems == null) return;
            
            var toRemove = new List<VideoItem>();
            foreach (VideoItem f in selectedItems) toRemove.Add(f);
            foreach (var f in toRemove) {
                AddedFiles.Remove(f);
            }
            UpdateButtons();
            SaveSession();
        }

        private void ClearAll(object obj) {
            AddedFiles.Clear();
            UpdateButtons();
            SaveSession();
        }

        private void Exit(object obj) {
            if (MessageBox.Show("Exit program? All hypnosis will be terminated :(", "Exit program", MessageBoxButton.YesNo) == MessageBoxResult.Yes) {
                SaveSession();
                Application.Current.Shutdown();
            }
        }

        private void Minimize(object obj) {
            if (obj is Window w) w.WindowState = WindowState.Minimized;
        }

        // Method to handle Drag & Drop from View
        public void AddDroppedFiles(string[] files) {
            // Safely execute async code from void method
            // This pattern ensures exceptions are properly caught and handled
            _ = AddDroppedFilesAsync(files).ContinueWith(task => {
                if (task.IsFaulted) {
                    var ex = task.Exception?.GetBaseException() ?? task.Exception;
                    Logger.Error("Error in AddDroppedFiles operation", ex);
                    Application.Current?.Dispatcher.InvokeAsync(() => {
                        SetStatusMessage($"Error adding dropped files: {ex?.Message ?? "Unknown error"}", StatusMessageType.Error);
                    });
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private async Task AddDroppedFilesAsync(string[] files) {
            // Filter to only video files
            var videoFiles = files.Where(f => {
                var ext = System.IO.Path.GetExtension(f)?.ToLowerInvariant();
                return Constants.VideoExtensions.Contains(ext);
            }).ToArray();
            
            if (videoFiles.Length > 0) {
                await AddFilesAsync(videoFiles, _cancellationTokenSource.Token);
            }
        }

        public void MoveVideoItem(VideoItem item, int newIndex) {
            if (item == null) return;
            var oldIndex = AddedFiles.IndexOf(item);
            if (oldIndex < 0 || newIndex < 0 || newIndex >= AddedFiles.Count) return;
            
            AddedFiles.Move(oldIndex, newIndex);
            SaveSession();
        }

        private void SavePlaylist(object obj) {
            var dlg = new SaveFileDialog {
                Filter = "TrainMeX Playlist|*.json",
                FileName = "playlist.json"
            };
            if (dlg.ShowDialog() == true) {
                var playlist = new Playlist();
                foreach (var item in AddedFiles) {
                    playlist.Items.Add(new PlaylistItem {
                        FilePath = item.FilePath,
                        ScreenDeviceName = item.AssignedScreen?.DeviceName,
                        Opacity = item.Opacity,
                        Volume = item.Volume,
                        Title = item.Title // Save title if available
                    });
                }
                
                var json = System.Text.Json.JsonSerializer.Serialize(playlist);
                System.IO.File.WriteAllText(dlg.FileName, json);
            }
        }

        private void LoadPlaylist(object obj) {
            var dlg = new OpenFileDialog {
                Filter = "TrainMeX Playlist|*.json"
            };
            if (dlg.ShowDialog() == true) {
                _ = LoadPlaylistAsync(dlg.FileName).ContinueWith(task => {
                    if (task.IsFaulted) {
                        var ex = task.Exception?.GetBaseException() ?? task.Exception;
                        Logger.Error("Error loading playlist", ex);
                        Application.Current?.Dispatcher.InvokeAsync(() => {
                            SetStatusMessage($"Failed to load playlist: {ex?.Message ?? "Unknown error"}", StatusMessageType.Error);
                        });
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }

        private async Task LoadPlaylistAsync(string fileName) {
            IsLoading = true;
            SetStatusMessage("Loading playlist...", StatusMessageType.Info);
            
            try {
                var json = await System.IO.File.ReadAllTextAsync(fileName);
                var playlist = System.Text.Json.JsonSerializer.Deserialize<Playlist>(json);
                
                if (playlist != null) {
                    await Application.Current.Dispatcher.InvokeAsync(() => {
                        AddedFiles.Clear();
                        if (AvailableScreens.Count == 0) RefreshScreens();
                    });
                    
                    var validItems = new List<VideoItem>();
                    var missingFiles = new List<string>();
                    var invalidFiles = new List<(string path, string error)>();
                    
                    foreach (var item in playlist.Items) {
                        await Application.Current.Dispatcher.InvokeAsync(() => {
                            var screen = AvailableScreens.FirstOrDefault(s => s.DeviceName == item.ScreenDeviceName) ?? AvailableScreens.FirstOrDefault();
                            var videoItem = new VideoItem(item.FilePath, screen);
                            videoItem.Opacity = item.Opacity;
                            videoItem.Volume = item.Volume;
                            
                            // Set title if available (backward compatible - Title may be null for old playlists)
                            if (!string.IsNullOrWhiteSpace(item.Title)) {
                                videoItem.Title = item.Title;
                            }
                            
                            // Validate the file
                            videoItem.Validate();
                            
                            if (videoItem.ValidationStatus == FileValidationStatus.Valid) {
                                validItems.Add(videoItem);
                            } else if (videoItem.ValidationStatus == FileValidationStatus.Missing) {
                                missingFiles.Add(System.IO.Path.GetFileName(videoItem.FilePath));
                            } else {
                                invalidFiles.Add((System.IO.Path.GetFileName(videoItem.FilePath), videoItem.ValidationError ?? "Invalid file"));
                            }
                            
                            // Always add to collection so user can see all files, including invalid ones
                            AddedFiles.Add(videoItem);
                        });
                    }
                    
                    await Application.Current.Dispatcher.InvokeAsync(() => {
                        UpdateButtons();
                        
                        // Show summary message
                        if (missingFiles.Count > 0 || invalidFiles.Count > 0) {
                            var messageParts = new List<string> { $"Loaded {validItems.Count} valid file(s)" };
                            if (missingFiles.Count > 0) {
                                messageParts.Add($"{missingFiles.Count} missing");
                            }
                            if (invalidFiles.Count > 0) {
                                messageParts.Add($"{invalidFiles.Count} invalid");
                            }
                            
                            var fileList = new List<string>();
                            fileList.AddRange(missingFiles);
                            fileList.AddRange(invalidFiles.Select(f => $"{f.path} ({f.error})"));
                            
                            var message = string.Join(", ", messageParts) + ".";
                            if (fileList.Count <= 5) {
                                message += "\n" + string.Join("\n", fileList);
                            } else {
                                message += $"\n{string.Join("\n", fileList.Take(5))}\n... and {fileList.Count - 5} more";
                            }
                            
                            SetStatusMessage(message, StatusMessageType.Warning);
                        } else {
                            SetStatusMessage($"Loaded {validItems.Count} file(s) from playlist", StatusMessageType.Success);
                        }
                    });
                }
            } catch (Exception ex) {
                Logger.Error("Error loading playlist", ex);
                await Application.Current.Dispatcher.InvokeAsync(() => {
                    SetStatusMessage($"Failed to load playlist: {ex.Message}", StatusMessageType.Error);
                });
            } finally {
                await Application.Current.Dispatcher.InvokeAsync(() => {
                    IsLoading = false;
                });
            }
        }
        private void SaveSession() {
            try {
                var playlistItems = AddedFiles.Select(item => new PlaylistItem {
                    FilePath = item.FilePath,
                    ScreenDeviceName = item.AssignedScreen?.DeviceName,
                    Opacity = item.Opacity,
                    Volume = item.Volume,
                    Title = item.Title // Save title if available
                }).ToList();

                System.Threading.Tasks.Task.Run(() => {
                    try {
                        var playlist = new Playlist { Items = playlistItems };
                        var json = System.Text.Json.JsonSerializer.Serialize(playlist);
                        var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "session.json");
                        System.IO.File.WriteAllText(path, json);
                    } catch (Exception ex) {
                        Logger.Error("Failed to save session in background", ex);
                    }
                });
            } catch (Exception ex) {
                Logger.Error("Failed to create session snapshot", ex);
            }
        }

        private async Task LoadSessionAsync(CancellationToken cancellationToken = default) {
            try {
                cancellationToken.ThrowIfCancellationRequested();
                
                var path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "session.json");
                if (!System.IO.File.Exists(path)) return;

                // Read file asynchronously to avoid blocking UI thread
                var json = await System.IO.File.ReadAllTextAsync(path, cancellationToken);
                
                cancellationToken.ThrowIfCancellationRequested();
                
                var playlist = System.Text.Json.JsonSerializer.Deserialize<Playlist>(json);
                
                if (playlist != null) {
                    // Dispatch UI updates back to UI thread
                    await Application.Current.Dispatcher.InvokeAsync(() => {
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        AddedFiles.Clear();
                        if (AvailableScreens.Count == 0) RefreshScreens();
                        
                        foreach (var item in playlist.Items) {
                            cancellationToken.ThrowIfCancellationRequested();
                            
                            var screen = AvailableScreens.FirstOrDefault(s => s.DeviceName == item.ScreenDeviceName) ?? AvailableScreens.FirstOrDefault(s => s.Screen.Primary) ?? AvailableScreens.FirstOrDefault();
                            var videoItem = new VideoItem(item.FilePath, screen);
                            videoItem.Opacity = item.Opacity;
                            videoItem.Volume = item.Volume;
                            
                            // Set title if available (backward compatible - Title may be null for old sessions)
                            if (!string.IsNullOrWhiteSpace(item.Title)) {
                                videoItem.Title = item.Title;
                            }
                            
                            // Validate the file when loading from session
                            videoItem.Validate();
                            AddedFiles.Add(videoItem);
                        }
                        UpdateButtons();
                    }, System.Windows.Threading.DispatcherPriority.Normal, cancellationToken);
                }
            } catch (OperationCanceledException) {
                Logger.Info("Load session operation was cancelled");
            } catch (Exception ex) {
                Logger.Warning("Failed to load session", ex);
            }
        }

        private bool _disposed = false;

        private void VideoService_MediaErrorOccurred(object sender, string errorMessage) {
            Application.Current?.Dispatcher.InvokeAsync(() => {
                SetStatusMessage(errorMessage, StatusMessageType.Error);
            });
        }

        /// <summary>
        /// Disposes resources and unsubscribes from events
        /// </summary>
        public void Dispose() {
            if (!_disposed) {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
                App.VideoService.MediaErrorOccurred -= VideoService_MediaErrorOccurred;
                _saveTimer?.Stop();
                _disposed = true;
            }
        }
    }
}
