using System;
using System.IO;
using System.Text.Json;

namespace GOON.Classes {

    public class UserSettings {
        public virtual double Opacity { get; set; } = 0.2;
        public virtual double Volume { get; set; } = 0.5;

        public bool LauncherAlwaysOnTop { get; set; } = false;
        public bool StartWithWindows { get; set; } = false;
        public double DefaultOpacity { get; set; } = 0.9;
        public double DefaultVolume { get; set; } = 0.5;
        public string DefaultMonitorDeviceName { get; set; } = null;
        public string HypnotubeCookies { get; set; } = null;
        public string UserAgent { get; set; } = null;
        public string Cookies { get; set; } = null;
        
        
        // Panic hotkey configuration
        // Modifiers: Ctrl=2, Shift=4, Alt=1 (can be combined with bitwise OR)
        public uint PanicHotkeyModifiers { get; set; } = 0x0002 | 0x0004; // Ctrl+Shift (default)
        public string PanicHotkeyKey { get; set; } = "End"; // Default key
        
        public string OpaquePanicHotkeyKey { get; set; } = "Escape";
        public uint OpaquePanicHotkeyModifiers { get; set; } = 0; // No modifiers by default

        public virtual bool AlwaysOpaque { get; set; } = false;
        public virtual bool EnableSuperResolution { get; set; } = false;
        
        // History Settings

        public virtual bool RememberLastPlaylist { get; set; } = true;
        public virtual bool RememberFilePosition { get; set; } = true;
        public System.Collections.Generic.List<string> PlayedHistory { get; set; } = new System.Collections.Generic.List<string>();
        public virtual bool VideoShuffle { get; set; } = true;
        

        // Playlist Import Settings
        public int MaxPlaylistPages { get; set; } = 100; // Maximum pages to fetch for paginated playlists

        // UI State
        public string LastExpandedSection { get; set; } = "IsPlaybackExpanded";


        public static string SettingsFilePath {
            get => AppPaths.SettingsFile;
            internal set => throw new InvalidOperationException("Path is now managed by AppPaths");
        }

        public static UserSettings Load() {
            try {
                // Migration Check 1: Old TrainMeX AppData to new GOON data directory
                var oldAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrainMeX");
                var newDataDir = AppPaths.DataDirectory;
                if (Directory.Exists(oldAppData) && !File.Exists(SettingsFilePath)) {
                    try {
                        Logger.Info("Migrating TrainMeX settings to GOON...");
                        foreach (var file in Directory.GetFiles(oldAppData)) {
                            var destFile = Path.Combine(newDataDir, Path.GetFileName(file));
                            if (!File.Exists(destFile)) {
                                File.Copy(file, destFile);
                            }
                        }
                    } catch (Exception ex) {
                        Logger.Warning("Failed to migrate settings from old AppData", ex);
                    }
                }

                // Migration Check 2: If local settings exist but AppData doesn't, migrate them
                var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
                if (File.Exists(localPath) && !File.Exists(SettingsFilePath)) {
                    try {
                        Logger.Info("Migrating legacy local settings to AppData...");
                        File.Copy(localPath, SettingsFilePath);
                        // Optional: File.Delete(localPath); // Keep for safety for now
                    } catch (Exception ex) {
                        Logger.Warning("Failed to migrate local settings", ex);
                    }
                }

                if (File.Exists(SettingsFilePath)) {
                    string json = File.ReadAllText(SettingsFilePath);
                    try {
                        var settings = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
                        // Validate and clamp loaded values
                        settings.ValidateAndClampValues();
                        Logger.Info($"Loaded settings from {SettingsFilePath}");
                        return settings;
                    } catch (JsonException jex) {
                        Logger.Warning($"Settings file at {SettingsFilePath} is corrupted. Renaming to .bak and using defaults.", jex);
                        try {
                            var bakPath = SettingsFilePath + ".bak";
                            if (File.Exists(bakPath)) File.Delete(bakPath);
                            File.Move(SettingsFilePath, bakPath);
                        } catch (Exception moveEx) {
                            Logger.Error("Failed to backup corrupted settings file", moveEx);
                        }
                        return new UserSettings();
                    }
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

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { 
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        private readonly System.Threading.SemaphoreSlim _saveLock = new System.Threading.SemaphoreSlim(1, 1);

        public void Save() {
            try {
                _saveLock.Wait();
                try {
                    string json = JsonSerializer.Serialize(this, _jsonOptions);
                    string tempPath = SettingsFilePath + ".tmp";
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, SettingsFilePath, true);
                } finally {
                    _saveLock.Release();
                }
            } catch (Exception ex) {
                Logger.Error("Failed to save settings (Sync)", ex);
            }
        }

        public async System.Threading.Tasks.Task SaveAsync() {
            try {
                await _saveLock.WaitAsync();
                try {
                    string json = JsonSerializer.Serialize(this, _jsonOptions);
                    string tempPath = SettingsFilePath + ".tmp";
                    await File.WriteAllTextAsync(tempPath, json);
                    File.Move(tempPath, SettingsFilePath, true);
                } finally {
                    _saveLock.Release();
                }
            } catch (Exception ex) {
                Logger.Error("Failed to save settings (Async)", ex);
            }
        }

        // --- Session Persistence ---

        // --- Session Persistence ---
        
        // Heavy data (Playlist) - Saved to session.json only on change
        [System.Text.Json.Serialization.JsonIgnore]
        public Playlist CurrentSessionPlaylist { get; set; } = new Playlist();

        // Light data (Position/Index) - Saved to settings.json continuously
        public PlaybackState LastPlaybackState { get; set; } = new PlaybackState();

        public static string SessionFilePath => AppPaths.SessionFile;

        private readonly System.Threading.SemaphoreSlim _sessionLock = new System.Threading.SemaphoreSlim(1, 1);

        public async System.Threading.Tasks.Task SaveSessionAsync() {
            try {
                if (CurrentSessionPlaylist == null) return;
                await _sessionLock.WaitAsync();
                try {
                    string json = JsonSerializer.Serialize(CurrentSessionPlaylist, _jsonOptions);
                    string tempPath = SessionFilePath + ".tmp";
                    await File.WriteAllTextAsync(tempPath, json);
                    File.Move(tempPath, SessionFilePath, true);
                } finally {
                    _sessionLock.Release();
                }
            } catch (Exception ex) {
                Logger.Warning("Failed to save session playlist (Async)", ex);
            }
        }

        public void SaveSession() {
            try {
                if (CurrentSessionPlaylist == null) return;
                _sessionLock.Wait();
                try {
                    string json = JsonSerializer.Serialize(CurrentSessionPlaylist, _jsonOptions);
                    string tempPath = SessionFilePath + ".tmp";
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, SessionFilePath, true);
                } finally {
                    _sessionLock.Release();
                }
            } catch (Exception ex) {
                Logger.Warning("Failed to save session playlist (Sync)", ex);
            }
        }

        public void LoadSession() {
            try {
                if (File.Exists(SessionFilePath)) {
                    string json = File.ReadAllText(SessionFilePath);
                    CurrentSessionPlaylist = JsonSerializer.Deserialize<Playlist>(json) ?? new Playlist();
                    Logger.Info("Loaded previous session playlist");
                }
            } catch (Exception ex) {
                Logger.Warning("Failed to load session playlist", ex);
                CurrentSessionPlaylist = new Playlist();
            }
        }
    }

    public class PlaybackState {
        public int CurrentIndex { get; set; } = 0;
        public long PositionTicks { get; set; } = 0;
        public double SpeedRatio { get; set; } = 1.0;
        public DateTime LastPlayed { get; set; } = DateTime.MinValue;
    }
}
