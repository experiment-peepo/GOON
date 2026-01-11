using System;
using System.IO;

namespace GOON.Classes {
    /// <summary>
    /// Centralized management of application data paths.
    /// Supports "Portable Mode" by checking for a local 'Data' folder.
    /// </summary>
    public static class AppPaths {
        private static string _dataDirectory;

        public static string DataDirectory {
            get {
                if (_dataDirectory == null) {
                    // Try to use local 'Data' folder for Portable Mode
                    var localData = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
                    
                    try {
                        // Create it if it doesn't exist
                        if (!Directory.Exists(localData)) {
                            Directory.CreateDirectory(localData);
                        }
                        
                        // If we can write to it, use it
                        if (Directory.Exists(localData)) {
                            _dataDirectory = localData;
                        }
                    } catch {
                        // If we can't create/access local 'Data' (e.g. Program Files), fallback to AppData
                        _dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GOON");
                        
                        if (!Directory.Exists(_dataDirectory)) {
                            Directory.CreateDirectory(_dataDirectory);
                        }
                    }
                }
                return _dataDirectory;
            }
        }

        public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
        public static string LogFile => Path.Combine(DataDirectory, "GOON.log");
        public static string SessionFile => Path.Combine(DataDirectory, "session.json");
        public static string CacheFile => Path.Combine(DataDirectory, "urlcache.json");
        public static string TelemetryFile => Path.Combine(DataDirectory, "telemetry.json");
        public static string PositionsFile => Path.Combine(DataDirectory, "playback_positions.json");
        public static string PlaylistsDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Playlists");
    }
}
