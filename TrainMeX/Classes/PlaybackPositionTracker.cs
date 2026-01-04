using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace TrainMeX.Classes {
    public class PlaybackPositionTracker {
        private const string PositionsFileName = "playback_positions.json";
        private static PlaybackPositionTracker _instance;
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
        private System.Windows.Threading.DispatcherTimer _saveTimer;

        // Map file path to position in ticks
        // Key is normalized path (lowercase)
        public Dictionary<string, long> Positions { get; set; } = new Dictionary<string, long>();

        public static PlaybackPositionTracker Instance => _instance ??= Load();

        private PlaybackPositionTracker() {
             // Initialize timer for periodic saving
             try {
                if (System.Windows.Application.Current != null) {
                    _saveTimer = new System.Windows.Threading.DispatcherTimer();
                    _saveTimer.Interval = TimeSpan.FromSeconds(10);
                    _saveTimer.Tick += (s, e) => Save();
                }
             } catch {}
        }

        public static PlaybackPositionTracker Load() {
            try {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PositionsFileName);
                if (File.Exists(path)) {
                    var json = File.ReadAllText(path);
                    var tracker = JsonSerializer.Deserialize<PlaybackPositionTracker>(json) ?? new PlaybackPositionTracker();
                    return tracker;
                }
            } catch (Exception ex) {
                Logger.Warning("Failed to load playback positions", ex);
            }
            return new PlaybackPositionTracker();
        }

        public void UpdatePosition(string filePath, TimeSpan position) {
            if (string.IsNullOrWhiteSpace(filePath) || App.Settings?.RememberFilePosition != true) return;
            
            // Only track if position is significant (> 5 seconds) and not near end
            // This prevents resuming just to finish the credits or loop immediately
            if (position.TotalSeconds < 5) return;

            var key = NormalizePath(filePath);
            lock (Positions) {
                Positions[key] = position.Ticks;
            }
            
            // Start timer if not running to batch saves
            if (_saveTimer != null && !_saveTimer.IsEnabled) {
                _saveTimer.Start();
            }
        }

        public TimeSpan? GetPosition(string filePath) {
            if (string.IsNullOrWhiteSpace(filePath) || App.Settings?.RememberFilePosition != true) return null;

            var key = NormalizePath(filePath);
            lock (Positions) {
                if (Positions.TryGetValue(key, out long ticks)) {
                    return TimeSpan.FromTicks(ticks);
                }
            }
            return null;
        }
        
        public void ClearPosition(string filePath) {
             var key = NormalizePath(filePath);
             lock (Positions) {
                 if (Positions.Remove(key)) {
                     Save();
                 }
             }
        }

        private string NormalizePath(string path) {
            return path.Trim().ToLowerInvariant();
        }

        public async void Save() {
            if (App.Settings?.RememberFilePosition != true) return;

            await _saveLock.WaitAsync();
            try {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PositionsFileName);
                string json;
                lock (Positions) {
                    json = JsonSerializer.Serialize(this);
                }
                await File.WriteAllTextAsync(path, json);
                
                if (_saveTimer != null) _saveTimer.Stop();
            } catch (Exception ex) {
                Logger.Error("Failed to save playback positions", ex);
            } finally {
                _saveLock.Release();
            }
        }
    }
}
