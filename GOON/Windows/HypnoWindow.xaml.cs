using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
// using System.Windows.Forms; // Removed to avoid ambiguity
using System.Windows.Media;
using System.Windows.Controls;
using GOON.Classes;
using GOON.ViewModels;
using System.Diagnostics;

namespace GOON.Windows {
    [SupportedOSPlatform("windows")]
    public partial class HypnoWindow : Window, IDisposable {
        private HypnoViewModel _viewModel;
        private System.Windows.Forms.Screen _targetScreen;
        private bool _disposed = false;
        private bool _isLoadingPosition = false;
        private bool _isFirstPlay = true;
        private TimeSpan? _pendingResumePosition = null;
        private DateTime _lastPositionSaveTime = DateTime.MinValue;
        private System.Windows.Threading.DispatcherTimer _syncTimer;
        private HotScreenBridge _hotScreenBridge;

        public HypnoViewModel ViewModel => _viewModel;
        public string ScreenDeviceName => _targetScreen?.DeviceName ?? "Unknown";

        public HypnoWindow(System.Windows.Forms.Screen screen = null) {
            InitializeComponent();
            _targetScreen = screen;
            _viewModel = new HypnoViewModel();
            this.DataContext = _viewModel;
            
            if (screen != null) {
                Logger.Info($"[HypnoWindow] Created for screen {screen.DeviceName}");
                
                // Set window position and size to cover the target screen
                this.Left = screen.Bounds.Left;
                this.Top = screen.Bounds.Top;
                this.Width = screen.Bounds.Width;
                this.Height = screen.Bounds.Height;
            } else {
                Logger.Info("[HypnoWindow] Created with default screen");
            }
            // ... (keep event subscriptions)
            _viewModel.RequestPlay += ViewModel_RequestPlay;
            _viewModel.RequestPause += ViewModel_RequestPause;
            _viewModel.RequestStop += ViewModel_RequestStop;
            _viewModel.RequestStopBeforeSourceChange += ViewModel_RequestStopBeforeSourceChange;
            _viewModel.MediaErrorOccurred += ViewModel_MediaErrorOccurred;
            _viewModel.RequestSyncPosition += ViewModel_RequestSyncPosition;
            _viewModel.TerminalFailure += ViewModel_TerminalFailure;
            
            // Initialize HotScreen integration (always enabled)
            _hotScreenBridge = new HotScreenBridge();
            
            // Update position when window moves/resizes
            this.LocationChanged += (s, e) => UpdateHotScreenPosition();
            this.SizeChanged += (s, e) => UpdateHotScreenPosition();
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            
            // Cleanup HotScreen bridge on close
            this.Closed += (s, e) => {
                if (_hotScreenBridge != null) {
                    var helper = new WindowInteropHelper(this);
                    var screen = System.Windows.Forms.Screen.FromHandle(helper.Handle);
                    int screenId = Array.IndexOf(System.Windows.Forms.Screen.AllScreens, screen);
                    _hotScreenBridge.ClearWindowPosition(screenId);
                    _hotScreenBridge.Dispose();
                }
            };

            // Initialize position reporting timer
            _syncTimer = new System.Windows.Threading.DispatcherTimer();
            _syncTimer.Interval = TimeSpan.FromMilliseconds(50);
            _syncTimer.Tick += SyncTimer_Tick;
            _syncTimer.Start();
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            if (_disposed || FirstVideo == null) return;
            if (e.PropertyName == nameof(HypnoViewModel.SpeedRatio)) {
                try {
                    Logger.Info($"Applying SpeedRatio: {_viewModel.SpeedRatio}");
                    FirstVideo.SpeedRatio = _viewModel.SpeedRatio;
                } catch (Exception ex) {
                   Logger.Warning($"Failed to set SpeedRatio: {ex.Message}");
                }
            }
        }

        private void SyncTimer_Tick(object sender, EventArgs e) {
            if (_disposed || FirstVideo == null || _viewModel == null) return;
            try {
                // Always update the LastPositionRecord for synchronization logic (every 50ms)
                if (FirstVideo.Source != null && FirstVideo.NaturalDuration.HasTimeSpan) {
                    _viewModel.LastPositionRecord = (FirstVideo.Position, Stopwatch.GetTimestamp());
                    
                    // But only update the PERSISTENT tracker every 2 seconds to avoid race conditions and overhead
                    if (DateTime.Now - _lastPositionSaveTime > TimeSpan.FromSeconds(2)) {
                        // Skip persistence if we are still in the protected 'loading/seeking' window
                        if (!_isLoadingPosition) {
                            string path = FirstVideo.Source.IsAbsoluteUri ? FirstVideo.Source.LocalPath : FirstVideo.Source.OriginalString;
                            PlaybackPositionTracker.Instance.UpdatePosition(path, FirstVideo.Position);
                            _lastPositionSaveTime = DateTime.Now;
                        }
                    }
                }
            } catch {
                // Ignore errors during position extraction
            }
        }

        private void ViewModel_RequestSyncPosition(object sender, TimeSpan position) {
            if (_disposed || FirstVideo == null) return;
            try {
                // Only sync if skew is significant (e.g. > 50ms) to avoid stuttering
                var diff = Math.Abs((FirstVideo.Position - position).TotalMilliseconds);
                if (diff > 50) {
                    FirstVideo.Position = position;
                }
            } catch {
                // Ignore sync errors
            }
        }
        
        private void ViewModel_RequestPlay(object sender, EventArgs e) {
            // Check disposal state and MediaElement availability
            if (_disposed || FirstVideo == null) return;
            
            try {
                // Only play if MediaElement has a source and is in a valid state
                // Double-check disposal state after the initial check to handle race conditions
                if (!_disposed && FirstVideo != null && FirstVideo.Source != null) {
                    FirstVideo.Play();

                    // DOUBLE-SEEK STRATEGY: 
                    // Some decoders (especially with 4K files) reset Position to 0 on the first Play call.
                    // By re-applying the seek right after Play(), we ensure it "sticks" to the active stream.
                    if (_isFirstPlay && _pendingResumePosition.HasValue) {
                        Logger.Info($"[HypnoWindow] Applying pending resume position on first Play: {_pendingResumePosition.Value}");
                        _isLoadingPosition = true;
                        FirstVideo.Position = _pendingResumePosition.Value;
                        _isFirstPlay = false;

                        // Keep the _isLoadingPosition guard active for 1 second to let the media pipeline stabilize
                        var guardTimer = new System.Windows.Threading.DispatcherTimer { 
                           Interval = TimeSpan.FromSeconds(1) 
                        };
                        guardTimer.Tick += (s, ev) => { 
                           _isLoadingPosition = false; 
                           guardTimer.Stop(); 
                        };
                        guardTimer.Start();
                    }
                }
            } catch (InvalidOperationException ex) {
                // MediaElement may be in an invalid state (e.g., disposed)
                Logger.Warning("MediaElement operation failed - may be disposed or in invalid state", ex);
            } catch (Exception ex) {
                Logger.Error("Error in ViewModel_RequestPlay", ex);
            }
        }

        private void ViewModel_RequestPause(object sender, EventArgs e) {
            // Check disposal state and MediaElement availability
            if (_disposed || FirstVideo == null) return;
            
            try {
                // Double-check disposal state after the initial check to handle race conditions
                if (!_disposed && FirstVideo != null) {
                    FirstVideo.Pause();
                }
            } catch (InvalidOperationException ex) {
                // MediaElement may be in an invalid state (e.g., disposed)
                Logger.Warning("MediaElement operation failed - may be disposed or in invalid state", ex);
            } catch (Exception ex) {
                Logger.Error("Error in ViewModel_RequestPause", ex);
            }
        }

        private void ViewModel_RequestStop(object sender, EventArgs e) {
            // Check disposal state and MediaElement availability
            if (_disposed || FirstVideo == null) return;
            
            try {
                // Double-check disposal state after the initial check to handle race conditions
                if (!_disposed && FirstVideo != null) {
                    FirstVideo.Stop();
                    FirstVideo.Close();
                }
            } catch (InvalidOperationException ex) {
                // MediaElement may be in an invalid state (e.g., disposed)
                Logger.Warning("MediaElement operation failed - may be disposed or in invalid state", ex);
            } catch (Exception ex) {
                Logger.Error("Error in ViewModel_RequestStop", ex);
            }
        }

        private void ViewModel_RequestStopBeforeSourceChange(object sender, EventArgs e) {
            // Check disposal state and MediaElement availability
            if (_disposed || FirstVideo == null) return;
            
            try {
                // Double-check disposal state after the initial check to handle race conditions
                // Stop the current video before changing source to ensure MediaEnded fires reliably
                // This prevents WPF MediaElement from missing MediaEnded events when Source changes
                // Stop() is synchronous and will complete before the source change happens
                if (!_disposed && FirstVideo != null) {
                    FirstVideo.Stop();
                }
            } catch (InvalidOperationException ex) {
                // MediaElement may be in an invalid state (e.g., disposed)
                Logger.Warning("MediaElement operation failed - may be disposed or in invalid state", ex);
            } catch (Exception ex) {
                Logger.Error("Error in ViewModel_RequestStopBeforeSourceChange", ex);
            }
        }


        protected virtual void Dispose(bool disposing) {
            if (!_disposed) {
                if (disposing) {
                    // Unsubscribe from events
                    if (_viewModel != null) {
                        _viewModel.RequestPlay -= ViewModel_RequestPlay;
                        _viewModel.RequestPause -= ViewModel_RequestPause;
                        _viewModel.RequestStop -= ViewModel_RequestStop;
                        _viewModel.RequestStopBeforeSourceChange -= ViewModel_RequestStopBeforeSourceChange;
                        _viewModel.MediaErrorOccurred -= ViewModel_MediaErrorOccurred;
                        _viewModel.RequestSyncPosition -= ViewModel_RequestSyncPosition;
                        _viewModel.TerminalFailure -= ViewModel_TerminalFailure;
                        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                    }

                    // Unregister from VideoService before closing/disposing
                    App.VideoService?.UnregisterPlayer(this);

                    if (_syncTimer != null) {
                        _syncTimer.Stop();
                        _syncTimer = null;
                    }
                    
                    // Dispose MediaElement
                    if (FirstVideo != null) {
                        try {
                            // LAST CHANCE: Capture position before stopping
                            // Capture final position before stopping MediaElement
                            // We use _isLoadingPosition guard to ensure we don't save 0 while seeking
                            if (!_isLoadingPosition && FirstVideo != null && FirstVideo.Source != null && FirstVideo.Position.TotalSeconds > 5) {
                                string path = FirstVideo.Source.IsAbsoluteUri ? FirstVideo.Source.LocalPath : FirstVideo.Source.OriginalString;
                                PlaybackPositionTracker.Instance.UpdatePosition(path, FirstVideo.Position);
                                Logger.Info($"[HypnoWindow] Captured final playback position for {System.IO.Path.GetFileName(path)}: {FirstVideo.Position}");
                            } else if (_isLoadingPosition) {
                                Logger.Info("[HypnoWindow] Skipping final position capture: loading/seeking in progress.");
                            } else if (FirstVideo != null && FirstVideo.Position.TotalSeconds <= 5) {
                                Logger.Info($"[HypnoWindow] Skipping capture: position too early ({FirstVideo.Position.TotalSeconds:F1}s)");
                            } else {
                                Logger.Info("[HypnoWindow] Skipping capture: FirstVideo or Source is null.");
                            }
                        } catch (Exception ex) {
                            Logger.Warning($"[HypnoWindow] Failed to capture final position: {ex.Message}");
                        }

                        FirstVideo.Stop();
                        FirstVideo.Close();
                        
                        // Phase 5 Optimization: Explicitly null the Source to release Media Foundation handles
                        // This ensures all unmanaged resources are freed, preventing memory leaks
                        FirstVideo.Source = null;
                        
                        FirstVideo = null;
                        Logger.Info("[HypnoWindow] MediaElement stopped, closed, and source cleared");
                    }
                }
                _disposed = true;
                Logger.Info("[HypnoWindow] Disposed");
            }
        }

        public void Dispose() {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected override void OnClosed(EventArgs e) {

             Dispose();
             base.OnClosed(e);
        }

        private void FirstVideo_MediaEnded(object sender, RoutedEventArgs e) {
            if (_disposed || _viewModel == null) return;
            
            try {
                if (FirstVideo != null && FirstVideo.Source != null) {
                    string path = FirstVideo.Source.IsAbsoluteUri ? FirstVideo.Source.LocalPath : FirstVideo.Source.OriginalString;
                    PlaybackPositionTracker.Instance.ClearPosition(path);
                }
                
                _viewModel.OnMediaEnded();
            } catch (Exception ex) {
                Logger.Error("Error in FirstVideo_MediaEnded", ex);
            }
        }

        private void FirstVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e) {
            if (_disposed || _viewModel == null) return;
            
            try {
                if (FirstVideo != null && FirstVideo.Source != null) {
                    var uri = FirstVideo.Source;
                    if (uri.IsAbsoluteUri) {
                        if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) {
                            App.Telemetry?.LogUrlFailure(uri.Host);
                        } else {
                            App.Telemetry?.LogFormatFailure(System.IO.Path.GetExtension(uri.LocalPath));
                        }
                    }
                }
                _viewModel.OnMediaFailed(e.ErrorException);
            } catch (Exception ex) {
                Logger.Error("Error in FirstVideo_MediaFailed", ex);
            }
        }

        private void ViewModel_MediaErrorOccurred(object sender, MediaErrorEventArgs e) {
            if (_disposed) return;
            
            try {
                // Forward the error to the VideoPlayerService so it can notify subscribers
                App.VideoService?.OnMediaError(e.ErrorMessage);
            } catch (Exception ex) {
                Logger.Error("Error in ViewModel_MediaErrorOccurred", ex);
            }
        }

        private void ViewModel_TerminalFailure(object sender, EventArgs e) {
            if (_disposed) return;
            
            // Terminal failure means all videos failed. Close the window.
            // Dispatch to UI thread just in case it's called from a background task
            Dispatcher.InvokeAsync(() => {
                if (!_disposed) {
                    Logger.Info("[HypnoWindow] Terminal failure occurred. Closing window.");
                    this.Close();
                }
            });
        }

        private void FirstVideo_MediaOpened(object sender, RoutedEventArgs e) {
            // Check disposal state and dependencies
            if (_disposed || _viewModel == null || FirstVideo == null) return;

            // Reset flags for the new media source
            _isFirstPlay = true;
            _pendingResumePosition = null;
            _lastPositionSaveTime = DateTime.Now; // Delay first persistence save
            
            try {
                // Double-check disposal state to handle race conditions
                if (!_disposed && _viewModel != null && FirstVideo != null) {
                    string path = FirstVideo.Source.IsAbsoluteUri ? FirstVideo.Source.LocalPath : FirstVideo.Source.OriginalString;
                    
                    // We wrap the restoration and playback in a BeginInvoke with Loaded priority.
                    // This ensures the MediaElement's internal pipeline is fully stable 
                    // before we attempt to seek or play, which is critical for 4K files.
                    Dispatcher.BeginInvoke(new Action(() => {
                        if (_disposed || FirstVideo == null || FirstVideo.Source == null) return;
                        
                        Logger.Info($"[HypnoWindow] Processing MediaOpened for: {System.IO.Path.GetFileName(path)}");

                        // 1. Check for saved position (Seek attempt 1)
                        try {
                            var savedPos = PlaybackPositionTracker.Instance.GetPosition(path);
                            if (savedPos.HasValue && savedPos.Value.Ticks > 0) {
                                _pendingResumePosition = savedPos.Value;

                                if (FirstVideo.NaturalDuration.HasTimeSpan) {
                                    TimeSpan duration = FirstVideo.NaturalDuration.TimeSpan;
                                    if (duration.Ticks > 0 && savedPos.Value < duration) {
                                        Logger.Info($"[HypnoWindow] First seek attempt (Open): {savedPos.Value}");
                                        _isLoadingPosition = true;
                                        FirstVideo.Position = savedPos.Value;
                                        _isLoadingPosition = false;
                                    }
                                }
                            }
                        } catch (Exception ex) {
                            Logger.Warning($"[HypnoWindow] Failed initial resume attempt: {ex.Message}");
                        }

                        // 2. Notify view model that media opened successfully
                        // This will trigger RequestPlay which will call Play() and perform Seek attempt 2
                        _viewModel.OnMediaOpened();
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            } catch (Exception ex) {
                Logger.Error("Error in FirstVideo_MediaOpened", ex);
            }
        }

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_SHOWWINDOW = 0x0040;
        const uint SWP_FRAMECHANGED = 0x0020;
        const uint SWP_ASYNCWINDOWPOS = 0x4000;

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        const int SW_MAXIMIZE = 3;
        const int SW_SHOW = 5;

        [SupportedOSPlatform("windows")]
        private void Window_SourceInitialized(object sender, EventArgs e) {
            if (_targetScreen != null) {
                // Validate that the target screen still exists
                var allScreens = System.Windows.Forms.Screen.AllScreens;
                bool screenExists = allScreens.Any(s => s.DeviceName == _targetScreen.DeviceName);
                
                if (!screenExists) {
                    // Screen was disconnected, fallback to primary screen
                    Logger.Warning($"Target screen {_targetScreen.DeviceName} is no longer available, falling back to primary screen");
                    _targetScreen = System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens.FirstOrDefault();
                    
                    if (_targetScreen == null) {
                        Logger.Error("No screens available for window positioning");
                        return;
                    }
                }
                
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                
                // --- HARDWARE DIAGNOSTICS ---
                int renderingTier = (RenderCapability.Tier >> 16);
                Logger.Info($"[Hardware] Rendering Tier: {renderingTier} (Tier 2 = Full Hardware Acceleration)");
                if (renderingTier < 2) {
                    Logger.Warning("[Hardware] WPF is in software rendering mode. This will cause 4K stutter.");
                }

                // Force Hardware Rendering if possible (ensure not SoftWare only)
                var source = HwndSource.FromHwnd(hwnd);
                if (source != null && source.CompositionTarget != null) {
                    source.CompositionTarget.RenderMode = RenderMode.Default;
                }
                // ---------------------------

                // 1. Transparency
                int extendedStyle = WindowServices.GetWindowLong(hwnd, WindowServices.GWL_EXSTYLE);
                WindowServices.SetWindowLong(hwnd, WindowServices.GWL_EXSTYLE, extendedStyle | WindowServices.WS_EX_TRANSPARENT);

                // 2. Physical Placement
                var b = _targetScreen.Bounds;
                SetWindowPos(hwnd, new IntPtr(-1), b.Left, b.Top, b.Width, b.Height, 
                    SWP_NOZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW);

                // 3. WPF Metadata
                this.WindowStartupLocation = WindowStartupLocation.Manual;
                this.WindowState = WindowState.Normal;

                // 4. Delayed WPF logical sync for scaling
                this.Dispatcher.BeginInvoke(new Action(() => {
                    var dpi = VisualTreeHelper.GetDpi(this);
                    this.Left = b.Left / dpi.DpiScaleX;
                    this.Top = b.Top / dpi.DpiScaleY;
                    this.Width = b.Width / dpi.DpiScaleX;
                    this.Height = b.Height / dpi.DpiScaleY;
                }), System.Windows.Threading.DispatcherPriority.Loaded);


            }
        }
        
        private void UpdateHotScreenPosition() {
            _hotScreenBridge?.UpdateWindowPosition(this);
        }

    }
}
