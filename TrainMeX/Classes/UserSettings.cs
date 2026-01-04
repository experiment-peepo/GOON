using System;
using System.IO;
using System.Text.Json;

namespace TrainMeX.Classes {
    public class UserSettings {
        public double Opacity { get; set; } = 0.2;
        public double Volume { get; set; } = 0.5;

        public bool LauncherAlwaysOnTop { get; set; } = false;
        public double DefaultOpacity { get; set; } = 0.9;
        public double DefaultVolume { get; set; } = 0.5;
        public string DefaultMonitorDeviceName { get; set; } = null;
        
        // Panic hotkey configuration
        // Modifiers: Ctrl=2, Shift=4, Alt=1 (can be combined with bitwise OR)
        public uint PanicHotkeyModifiers { get; set; } = 0x0002 | 0x0004; // Ctrl+Shift (default)
        public string PanicHotkeyKey { get; set; } = "End"; // Default key
        public bool AlwaysOpaque { get; set; } = false;
        
        // History Settings

        public bool RememberLastPlaylist { get; set; } = true;
        public bool RememberFilePosition { get; set; } = false;

        // Taboo Settings




        public static string SettingsFilePath { get; set; } = "settings.json";

        public static UserSettings Load() {
            try {
                if (File.Exists(SettingsFilePath)) {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
                    // Validate and clamp loaded values
                    settings.ValidateAndClampValues();
                    return settings;
                }
            } catch (Exception ex) {
                Logger.Warning("Failed to load settings, using defaults", ex);
            }
            return new UserSettings();
        }

        /// <summary>
        /// Validates and clamps opacity and volume values to valid ranges (0.0-1.0)
        /// </summary>
        private void ValidateAndClampValues() {
            bool valuesCorrected = false;
            
            if (Opacity < 0.0 || Opacity > 1.0) {
                var oldValue = Opacity;
                Opacity = Math.Max(0.0, Math.Min(1.0, Opacity));
                Logger.Warning($"Opacity value {oldValue} was out of range (0.0-1.0), clamped to {Opacity}");
                valuesCorrected = true;
            }
            
            if (Volume < 0.0 || Volume > 1.0) {
                var oldValue = Volume;
                Volume = Math.Max(0.0, Math.Min(1.0, Volume));
                Logger.Warning($"Volume value {oldValue} was out of range (0.0-1.0), clamped to {Volume}");
                valuesCorrected = true;
            }
            
            if (DefaultOpacity < 0.0 || DefaultOpacity > 1.0) {
                var oldValue = DefaultOpacity;
                DefaultOpacity = Math.Max(0.0, Math.Min(1.0, DefaultOpacity));
                Logger.Warning($"DefaultOpacity value {oldValue} was out of range (0.0-1.0), clamped to {DefaultOpacity}");
                valuesCorrected = true;
            }
            
            if (DefaultVolume < 0.0 || DefaultVolume > 1.0) {
                var oldValue = DefaultVolume;
                DefaultVolume = Math.Max(0.0, Math.Min(1.0, DefaultVolume));
                Logger.Warning($"DefaultVolume value {oldValue} was out of range (0.0-1.0), clamped to {DefaultVolume}");
                valuesCorrected = true;
            }
            
            if (valuesCorrected) {
                // Save corrected values back to file
                try {
                    Save();
                } catch (Exception ex) {
                    Logger.Warning("Failed to save corrected settings values", ex);
                }
            }
        }

        public void Save() {
            try {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            } catch (Exception ex) {
                Logger.Error("Failed to save settings", ex);
            }
        }
    }
}
