using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GOON.Classes {
    public class TelemetryData {
        public Dictionary<string, int> FormatFailures { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> UrlFailures { get; set; } = new Dictionary<string, int>();
        public DateTime LastUpdated { get; set; }
    }

    public class TelemetryService {
        private readonly string _telemetryPath;
        private TelemetryData _data;
        private readonly object _lock = new object();

        public TelemetryService() {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _telemetryPath = Path.Combine(appData, "GOON", "telemetry.json");
            Directory.CreateDirectory(Path.GetDirectoryName(_telemetryPath));
            _data = LoadTelemetry();
        }

        private TelemetryData LoadTelemetry() {
            try {
                if (File.Exists(_telemetryPath)) {
                    string json = File.ReadAllText(_telemetryPath);
                    return JsonSerializer.Deserialize<TelemetryData>(json) ?? new TelemetryData();
                }
            } catch (Exception ex) {
                Logger.Warning("Failed to load telemetry", ex);
            }
            return new TelemetryData();
        }

        public void SaveTelemetry() {
            lock (_lock) {
                try {
                    _data.LastUpdated = DateTime.Now;
                    string json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_telemetryPath, json);
                } catch (Exception ex) {
                    Logger.Warning("Failed to save telemetry", ex);
                }
            }
        }

        public void LogFormatFailure(string format) {
            lock (_lock) {
                if (string.IsNullOrEmpty(format)) format = "unknown";
                if (!_data.FormatFailures.ContainsKey(format)) _data.FormatFailures[format] = 0;
                _data.FormatFailures[format]++;
                SaveTelemetry();
            }
        }

        public void LogUrlFailure(string host) {
            lock (_lock) {
                if (string.IsNullOrEmpty(host)) host = "unknown";
                if (!_data.UrlFailures.ContainsKey(host)) _data.UrlFailures[host] = 0;
                _data.UrlFailures[host]++;
                SaveTelemetry();
            }
        }
    }
}
