using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
// using System.Windows.Forms; // Removed to avoid ambiguity
using System.Windows.Media;
using System.Windows.Controls;
using TrainMeX.Classes;
using TrainMeX.ViewModels;
using System.Diagnostics;

namespace TrainMeX.Windows {
    [SupportedOSPlatform("windows")]
    public partial class HypnoWindow : Window, IDisposable {
        private HypnoViewModel _viewModel;
        private System.Windows.Forms.Screen _targetScreen;
        private bool _disposed = false;
        private System.Windows.Threading.DispatcherTimer _syncTimer;

        public HypnoWindow(System.Windows.Forms.Screen targetScreen = null) {
            InitializeComponent();
            _targetScreen = targetScreen;
            
            // GPU acceleration is enabled at application level in App.xaml.cs
            // WPF MediaElement uses Windows Media Foundation which automatically uses hardware acceleration
            
            _viewModel = new HypnoViewModel();
            DataContext = _viewModel;
            // ... (keep event subscriptions)
            _viewModel.RequestPlay += ViewModel_RequestPlay;
            _viewModel.RequestPause += ViewModel_RequestPause;
            _viewModel.RequestStop += ViewModel_RequestStop;
            _viewModel.RequestStopBeforeSourceChange += ViewModel_RequestStopBeforeSourceChange;
            _viewModel.MediaErrorOccurred += ViewModel_MediaErrorOccurred;
            _viewModel.RequestSyncPosition += ViewModel_RequestSyncPosition;
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;

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
                // Only report position if media is loaded and playing
                if (FirstVideo.Source != null && FirstVideo.NaturalDuration.HasTimeSpan) {
                    _viewModel.LastPositionRecord = (FirstVideo.Position, Stopwatch.GetTimestamp());
                    
                    // Update playback position tracker
                    string path = FirstVideo.Source.IsAbsoluteUri ? FirstVideo.Source.LocalPath : FirstVideo.Source.OriginalString;
                    PlaybackPositionTracker.Instance.UpdatePosition(path, FirstVideo.Position);
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

        public HypnoViewModel ViewModel => _viewModel;

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
                        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                    }

                    if (_syncTimer != null) {
                        _syncTimer.Stop();
                        _syncTimer = null;
                    }
                    
                    // Dispose MediaElement
                    if (FirstVideo != null) {
                        FirstVideo.Stop();
                        FirstVideo.Close();
                        FirstVideo = null;
                    }
                }
                _disposed = true;
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

        private void FirstVideo_MediaOpened(object sender, RoutedEventArgs e) {
            // Check disposal state and dependencies
            if (_disposed || _viewModel == null || FirstVideo == null) return;
            
            try {
                // Double-check disposal state to handle race conditions
                if (!_disposed && _viewModel != null && FirstVideo != null) {
                    // Notify view model that media opened successfully
                    // This will trigger RequestPlay event which will call Play()
                    _viewModel.OnMediaOpened();
                    
                    // Check for saved position
                    if (FirstVideo.Source != null) {
                        try {
                            // Using LocalPath if absolute URI, or OriginalString
                            string path = FirstVideo.Source.IsAbsoluteUri ? FirstVideo.Source.LocalPath : FirstVideo.Source.OriginalString;
                            var savedPos = PlaybackPositionTracker.Instance.GetPosition(path);
                            if (savedPos.HasValue) {
                                Logger.Info($"Resuming video from saved position: {savedPos.Value}");
                                FirstVideo.Position = savedPos.Value;
                            }
                        } catch (Exception ex) {
                            Logger.Warning($"Failed to restore playback position: {ex.Message}");
                        }
                    }

                    // Note: Play() is now called via RequestPlay event from OnMediaOpened()
                    // This ensures proper sequencing and state management
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
        

    }
}
