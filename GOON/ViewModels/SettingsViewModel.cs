using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Forms;
using System.Windows.Input;
using GOON.Classes;
using System.IO;

namespace GOON.ViewModels {
    /// <summary>
    /// ViewModel for the Settings window
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class SettingsViewModel : ObservableObject {
        private double _defaultOpacity;
        private double _defaultVolume;

        private bool _launcherAlwaysOnTop;
        private bool _startWithWindows;
        private bool _panicHotkeyCtrl;
        private bool _panicHotkeyShift;
        private bool _panicHotkeyAlt;
        private string _panicHotkeyKey;
        private ScreenViewer _selectedDefaultMonitor;
        private bool _alwaysOpaque;

        private bool _rememberLastPlaylist;
        private bool _rememberFilePosition;
        private bool _isPlaybackExpanded;
        private bool _isApplicationExpanded;
        private bool _isHotkeysExpanded;
        private bool _isHistoryExpanded;

        public bool IsPlaybackExpanded {
            get => _isPlaybackExpanded;
            set {
                if (SetProperty(ref _isPlaybackExpanded, value) && value) {
                    CollapseOthers(nameof(IsPlaybackExpanded));
                    App.Settings.LastExpandedSection = nameof(IsPlaybackExpanded);
                }
            }
        }

        public bool IsApplicationExpanded {
            get => _isApplicationExpanded;
            set {
                if (SetProperty(ref _isApplicationExpanded, value) && value) {
                    CollapseOthers(nameof(IsApplicationExpanded));
                    App.Settings.LastExpandedSection = nameof(IsApplicationExpanded);
                }
            }
        }

        public bool IsHotkeysExpanded {
            get => _isHotkeysExpanded;
            set {
                if (SetProperty(ref _isHotkeysExpanded, value) && value) {
                    CollapseOthers(nameof(IsHotkeysExpanded));
                    App.Settings.LastExpandedSection = nameof(IsHotkeysExpanded);
                }
            }
        }

        public bool IsHistoryExpanded {
            get => _isHistoryExpanded;
            set {
                if (SetProperty(ref _isHistoryExpanded, value) && value) {
                    CollapseOthers(nameof(IsHistoryExpanded));
                    App.Settings.LastExpandedSection = nameof(IsHistoryExpanded);
                }
            }
        }

        private void CollapseOthers(string current) {
            if (current != nameof(IsPlaybackExpanded)) IsPlaybackExpanded = false;
            if (current != nameof(IsApplicationExpanded)) IsApplicationExpanded = false;
            if (current != nameof(IsHotkeysExpanded)) IsHotkeysExpanded = false;
            if (current != nameof(IsHistoryExpanded)) IsHistoryExpanded = false;
        }

        // Taboo Settings


        // Modifier flags
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_ALT = 0x0001;

        [SupportedOSPlatform("windows")]
        public SettingsViewModel() {
            // Load current settings
            var settings = App.Settings;
            _defaultOpacity = settings.DefaultOpacity;
            _defaultVolume = settings.DefaultVolume;

            _launcherAlwaysOnTop = settings.LauncherAlwaysOnTop;
            _startWithWindows = StartupManager.IsStartupEnabled();
            
            // Load panic hotkey settings
            _panicHotkeyCtrl = (settings.PanicHotkeyModifiers & MOD_CONTROL) != 0;
            _panicHotkeyShift = (settings.PanicHotkeyModifiers & MOD_SHIFT) != 0;
            _panicHotkeyAlt = (settings.PanicHotkeyModifiers & MOD_ALT) != 0;
            _panicHotkeyKey = settings.PanicHotkeyKey ?? "End";
            _alwaysOpaque = settings.AlwaysOpaque;

            _rememberLastPlaylist = settings.RememberLastPlaylist;
            _rememberFilePosition = settings.RememberFilePosition;

            // Load and set the last expanded section
            var lastSection = settings.LastExpandedSection ?? nameof(IsPlaybackExpanded);
            _isPlaybackExpanded = lastSection == nameof(IsPlaybackExpanded);
            _isApplicationExpanded = lastSection == nameof(IsApplicationExpanded);
            _isHotkeysExpanded = lastSection == nameof(IsHotkeysExpanded);
            _isHistoryExpanded = lastSection == nameof(IsHistoryExpanded);

            // Ensure at least one is expanded if the loaded value was invalid
            if (!_isPlaybackExpanded && !_isApplicationExpanded && !_isHotkeysExpanded && !_isHistoryExpanded) {
                _isPlaybackExpanded = true;
            }



            // Load available monitors
            AvailableMonitors = new ObservableCollection<ScreenViewer>();
            RefreshAvailableMonitors();
            
            // Load default monitor from settings
            if (!string.IsNullOrEmpty(settings.DefaultMonitorDeviceName)) {
                _selectedDefaultMonitor = AvailableMonitors.FirstOrDefault(m => m.DeviceName == settings.DefaultMonitorDeviceName);
            }

            OkCommand = new RelayCommand(Ok);
            CancelCommand = new RelayCommand(Cancel);
            OpenKoFiCommand = new RelayCommand(OpenKoFi);
            ResetPositionsCommand = new RelayCommand(ResetPositions);
        }

        private void ResetPositions(object obj) {
            if (Windows.ConfirmationDialog.Show("Are you sure you want to clear all saved video positions? This cannot be undone.", "Reset Playback History")) {
                PlaybackPositionTracker.Instance.ClearAllPositions();
            }
        }

        private void OpenKoFi(object obj) {
            try {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
                    FileName = "https://ko-fi.com/vexfromdestiny",
                    UseShellExecute = true
                });
            } catch (System.Exception ex) {
                Logger.Error("Failed to open Ko-Fi link", ex);
            }
        }

        [SupportedOSPlatform("windows")]
        private void RefreshAvailableMonitors() {
            AvailableMonitors.Clear();
            try {
                // Add "All Screens" option first
                AvailableMonitors.Add(ScreenViewer.CreateAllScreens());
                
                var screens = WindowServices.GetAllScreenViewers();
                foreach (var screen in screens) {
                    AvailableMonitors.Add(screen);
                }
            } catch (System.Exception ex) {
                Logger.Warning("Failed to load monitors for settings", ex);
            }
        }

        public ObservableCollection<ScreenViewer> AvailableMonitors { get; }

        public ScreenViewer SelectedDefaultMonitor {
            get => _selectedDefaultMonitor;
            set => SetProperty(ref _selectedDefaultMonitor, value);
        }

        public double DefaultOpacity {
            get => _defaultOpacity;
            set => SetProperty(ref _defaultOpacity, value);
        }

        public double DefaultVolume {
            get => _defaultVolume;
            set => SetProperty(ref _defaultVolume, value);
        }



        public bool LauncherAlwaysOnTop {
            get => _launcherAlwaysOnTop;
            set => SetProperty(ref _launcherAlwaysOnTop, value);
        }

        public bool StartWithWindows {
            get => _startWithWindows;
            set => SetProperty(ref _startWithWindows, value);
        }

        public bool PanicHotkeyCtrl {
            get => _panicHotkeyCtrl;
            set {
                SetProperty(ref _panicHotkeyCtrl, value);
                OnPropertyChanged(nameof(PanicHotkeyDisplay));
            }
        }

        public bool PanicHotkeyShift {
            get => _panicHotkeyShift;
            set {
                SetProperty(ref _panicHotkeyShift, value);
                OnPropertyChanged(nameof(PanicHotkeyDisplay));
            }
        }

        public bool PanicHotkeyAlt {
            get => _panicHotkeyAlt;
            set {
                SetProperty(ref _panicHotkeyAlt, value);
                OnPropertyChanged(nameof(PanicHotkeyDisplay));
            }
        }

        public string PanicHotkeyKey {
            get => _panicHotkeyKey;
            set {
                SetProperty(ref _panicHotkeyKey, value);
                OnPropertyChanged(nameof(PanicHotkeyDisplay));
            }
        }

        public string PanicHotkeyDisplay {
            get {
                var parts = new System.Collections.Generic.List<string>();
                if (PanicHotkeyCtrl) parts.Add("Ctrl");
                if (PanicHotkeyShift) parts.Add("Shift");
                if (PanicHotkeyAlt) parts.Add("Alt");
                
                parts.Add(PanicHotkeyKey ?? "End");
                return string.Join("+", parts);
            }
        }

        public bool AlwaysOpaque {
            get => _alwaysOpaque;
            set => SetProperty(ref _alwaysOpaque, value);
        }



        public bool RememberLastPlaylist {
            get => _rememberLastPlaylist;
            set => SetProperty(ref _rememberLastPlaylist, value);
        }

        public bool RememberFilePosition {
            get => _rememberFilePosition;
            set => SetProperty(ref _rememberFilePosition, value);
        }

            
        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand OpenKoFiCommand { get; }
        public ICommand ResetPositionsCommand { get; }

        public event System.EventHandler RequestClose;

        private void Ok(object obj) {
            // Save settings
            var settings = App.Settings;
            settings.DefaultOpacity = DefaultOpacity;
            settings.DefaultVolume = DefaultVolume;

            settings.LauncherAlwaysOnTop = LauncherAlwaysOnTop;
            
            // Apply startup setting to Registry
            StartupManager.SetStartup(StartWithWindows);
            settings.StartWithWindows = StartWithWindows;
            
            // Save default monitor
            settings.DefaultMonitorDeviceName = SelectedDefaultMonitor?.DeviceName;
            
            // Save panic hotkey settings
            uint modifiers = 0;
            if (PanicHotkeyCtrl) modifiers |= MOD_CONTROL;
            if (PanicHotkeyShift) modifiers |= MOD_SHIFT;
            if (PanicHotkeyAlt) modifiers |= MOD_ALT;
            settings.PanicHotkeyModifiers = modifiers;
            settings.PanicHotkeyKey = PanicHotkeyKey ?? "End";
            settings.AlwaysOpaque = AlwaysOpaque;

            settings.RememberLastPlaylist = RememberLastPlaylist;
            settings.RememberFilePosition = RememberFilePosition;
            
            // Save currently expanded section
            if (IsPlaybackExpanded) settings.LastExpandedSection = nameof(IsPlaybackExpanded);
            else if (IsApplicationExpanded) settings.LastExpandedSection = nameof(IsApplicationExpanded);
            else if (IsHotkeysExpanded) settings.LastExpandedSection = nameof(IsHotkeysExpanded);
            else if (IsHistoryExpanded) settings.LastExpandedSection = nameof(IsHistoryExpanded);
            
            settings.Save();

            RequestClose?.Invoke(this, System.EventArgs.Empty);
        }

        private void Cancel(object obj) {
            RequestClose?.Invoke(this, System.EventArgs.Empty);
        }

    }
}
