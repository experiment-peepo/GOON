using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows.Forms;
using System.Windows.Input;
using TrainMeX.Classes;

namespace TrainMeX.ViewModels {
    /// <summary>
    /// ViewModel for the Settings window
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class SettingsViewModel : ObservableObject {
        private double _defaultOpacity;
        private double _defaultVolume;
        private bool _autoLoadSession;
        private bool _launcherAlwaysOnTop;
        private bool _panicHotkeyCtrl;
        private bool _panicHotkeyShift;
        private bool _panicHotkeyAlt;
        private string _panicHotkeyKey;
        private ScreenViewer _selectedDefaultMonitor;
        private bool _alwaysOpaque;

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
            _autoLoadSession = settings.AutoLoadSession;
            _launcherAlwaysOnTop = settings.LauncherAlwaysOnTop;
            
            // Load panic hotkey settings
            _panicHotkeyCtrl = (settings.PanicHotkeyModifiers & MOD_CONTROL) != 0;
            _panicHotkeyShift = (settings.PanicHotkeyModifiers & MOD_SHIFT) != 0;
            _panicHotkeyAlt = (settings.PanicHotkeyModifiers & MOD_ALT) != 0;
            _panicHotkeyKey = settings.PanicHotkeyKey ?? "End";
            _alwaysOpaque = settings.AlwaysOpaque;


            // Load available monitors
            AvailableMonitors = new ObservableCollection<ScreenViewer>();
            RefreshAvailableMonitors();
            
            // Load default monitor from settings
            if (!string.IsNullOrEmpty(settings.DefaultMonitorDeviceName)) {
                _selectedDefaultMonitor = AvailableMonitors.FirstOrDefault(m => m.DeviceName == settings.DefaultMonitorDeviceName);
            }

            OkCommand = new RelayCommand(Ok);
            CancelCommand = new RelayCommand(Cancel);
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

        public bool AutoLoadSession {
            get => _autoLoadSession;
            set => SetProperty(ref _autoLoadSession, value);
        }

        public bool LauncherAlwaysOnTop {
            get => _launcherAlwaysOnTop;
            set => SetProperty(ref _launcherAlwaysOnTop, value);
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

        public ICommand OkCommand { get; }
        public ICommand CancelCommand { get; }

        public event System.EventHandler RequestClose;

        private void Ok(object obj) {
            // Save settings
            var settings = App.Settings;
            settings.DefaultOpacity = DefaultOpacity;
            settings.DefaultVolume = DefaultVolume;
            settings.AutoLoadSession = AutoLoadSession;
            settings.LauncherAlwaysOnTop = LauncherAlwaysOnTop;
            
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
            

            
            settings.Save();

            RequestClose?.Invoke(this, System.EventArgs.Empty);
        }

        private void Cancel(object obj) {
            RequestClose?.Invoke(this, System.EventArgs.Empty);
        }

}
}

