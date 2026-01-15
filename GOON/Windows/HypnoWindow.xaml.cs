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
using Logger = GOON.Classes.Logger;

namespace GOON.Windows {
    [SupportedOSPlatform("windows")]
    public partial class HypnoWindow : Window, IDisposable {
        private HypnoViewModel _viewModel;
        private System.Windows.Forms.Screen _targetScreen;
        private bool _disposed = false;
        private DateTime _lastPositionSaveTime = DateTime.MinValue;
        private System.Windows.Threading.DispatcherTimer _syncTimer;


        public HypnoViewModel ViewModel => _viewModel;
        public string ScreenDeviceName => _targetScreen?.DeviceName ?? "Unknown";

        public HypnoWindow(System.Windows.Forms.Screen screen = null) {
            InitializeComponent();
            _targetScreen = screen;
            _viewModel = new HypnoViewModel(App.UrlExtractor);
            this.DataContext = _viewModel;
            
            if (screen != null) {
                Logger.Info($"[HypnoWindow] Created for screen {screen.DeviceName} | Bounds: {screen.Bounds}");
                
                // Set window position and size to cover the target screen
                this.Left = screen.Bounds.Left;
                this.Top = screen.Bounds.Top;
                this.Width = screen.Bounds.Width;
                this.Height = screen.Bounds.Height;
            } else {
                Logger.Info("[HypnoWindow] Created with default screen");
            }
            // ... (keep event subscriptions)
            _viewModel.MediaErrorOccurred += ViewModel_MediaErrorOccurred;
            _viewModel.TerminalFailure += ViewModel_TerminalFailure;
            

            

            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            


            // Initialize position reporting timer
            _syncTimer = new System.Windows.Threading.DispatcherTimer();
            _syncTimer.Interval = TimeSpan.FromMilliseconds(50);
            _syncTimer.Tick += SyncTimer_Tick;
            _syncTimer.Start();
        }

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e) {
            // SpeedRatio and Volume are now handled directly by the ViewModel's Player instance
        }

        private void SyncTimer_Tick(object sender, EventArgs e) {
            // Logic moved to HypnoViewModel.cs to centralize position tracking
        }

        // Removed ViewModel_Request* handlers as they are now handled by Player instance in VM


        protected virtual void Dispose(bool disposing) {
            if (!_disposed) {
                if (disposing) {
                    // Unsubscribe from events
                    if (_viewModel != null) {
                        _viewModel.MediaErrorOccurred -= ViewModel_MediaErrorOccurred;
                        _viewModel.TerminalFailure -= ViewModel_TerminalFailure;
                        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                        _viewModel.Dispose();
                    }

                    // Unregister from VideoService before closing/disposing
                    App.VideoService?.UnregisterPlayer(this);

                    if (_syncTimer != null) {
                        _syncTimer.Stop();
                        _syncTimer = null;
                    }
                    
                    // Dispose MediaElement
                    if (FirstVideo != null) {
                         // Flyleaf Player disposal is handled by ViewModel
                         FirstVideo = null;
                         Logger.Info("[HypnoWindow] Flyleaf control reference cleared");
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

        // Removed FirstVideo_MediaEnded/Failed as Flyleaf events are mapped in ViewModel

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

        // Removed FirstVideo_MediaOpened as it's now handled by ViewModel's Player.OpenCompleted

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
                Logger.Info($"[HypnoWindow] Positioning: {b.Left},{b.Top} {b.Width}x{b.Height}");
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
