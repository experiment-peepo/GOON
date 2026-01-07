using System;
using System.Reflection;
using Microsoft.Win32;

namespace GOON.Classes {
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static class StartupManager {
        private const string RegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "GOON";

        public static bool IsStartupEnabled() {
            try {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath)) {
                    if (key == null) return false;
                    return key.GetValue(AppName) != null;
                }
            } catch {
                return false;
            }
        }

        public static void SetStartup(bool enable) {
            try {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, true)) {
                    if (key == null) return;

                    if (enable) {
                        string exePath = Assembly.GetExecutingAssembly().Location;
                        key.SetValue(AppName, exePath);
                    } else {
                        key.DeleteValue(AppName, false);
                    }
                }
            } catch (Exception ex) {
                System.Windows.MessageBox.Show(
                    $"Failed to {(enable ? "enable" : "disable")} startup: {ex.Message}",
                    "Startup Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }
    }
}

