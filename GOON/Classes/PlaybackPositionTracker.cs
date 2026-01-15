using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GOON.Classes {
    /// <summary>
    /// Data model for storing playback position information
    /// </summary>
    public class FilePositionData {
        public string FilePath { get; set; }
        public long Position { get; set; } // Position in 100-nanosecond units (ticks)
        public DateTime LastUpdated { get; set; }
    }

    /// <summary>
    /// Manages saving and loading of video playback positions
    /// </summary>
    public class PlaybackPositionTracker {
        private const string PositionsFileName = "playback_positions.json";
        private static PlaybackPositionTracker _instance;
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
        private System.Windows.Threading.DispatcherTimer _saveTimer;
        private static string _settingsPath;

        static PlaybackPositionTracker() {
             _settingsPath = AppPaths.PositionsFile;
        }

        // Map file path to position data
        // Key is normalized path (lowercase full path)
        public Dictionary<string, FilePositionData> Positions { get; set; } = new Dictionary<string, FilePositionData>();
        
        // Cache used to store resolved paths significantly reducing Disk IO from Path.GetFullPath calls
        private static readonly ConcurrentDictionary<string, string> _pathKeyCache = new ConcurrentDictionary<string, string>();

        public static PlaybackPositionTracker Instance => _instance ??= Load();

        public PlaybackPositionTracker() {
             // Initialize timer for periodic saving
             try {
                if (System.Windows.Application.Current != null) {
                    _saveTimer = new System.Windows.Threading.DispatcherTimer();
                    _saveTimer.Interval = TimeSpan.FromSeconds(2);
                    _saveTimer.Tick += async (s, e) => await SaveAsync();
                    _saveTimer.Start();
                }
             } catch {}
        }

        public static PlaybackPositionTracker Load() {
            try {
                if (File.Exists(_settingsPath)) {
                    var json = File.ReadAllText(_settingsPath);
                    try {
                        var tracker = JsonSerializer.Deserialize<PlaybackPositionTracker>(json);
                        if (tracker != null && tracker.Positions != null) {
                            Logger.Info($"[PositionTracker] Loaded {tracker.Positions.Count} positions from data directory");
                            return tracker;
                        }
                    } catch (JsonException) {
                        Logger.Info("[PositionTracker] Detected legacy format in data directory, attempting conversion...");
                        var legacy = JsonSerializer.Deserialize<LegacyTracker>(json);
                        if (legacy?.Positions != null) {
                            var tracker = new PlaybackPositionTracker();
                            foreach (var kvp in legacy.Positions) {
                                tracker.Positions[kvp.Key] = new FilePositionData {
                                    FilePath = kvp.Key,
                                    Position = kvp.Value,
                                    LastUpdated = DateTime.Now
                                };
                            }
                            Logger.Debug($"[PositionTracker] Successfully saved {tracker.Positions.Count} playback positions in data directory");
                            return tracker;
                        }
                    }
                }
                
                // Legacy migration from base directory
                var legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PositionsFileName);
                if (File.Exists(legacyPath)) {
                    Logger.Info("[PositionTracker] Migrating legacy positions from base directory...");
                    var json = File.ReadAllText(legacyPath);
                    try {
                        var tracker = JsonSerializer.Deserialize<PlaybackPositionTracker>(json);
                        if (tracker != null) {
                            tracker.SaveSync();
                            try { File.Delete(legacyPath); } catch { }
                            return tracker;
                        }
                    } catch {
                        var legacy = JsonSerializer.Deserialize<LegacyTracker>(json);
                        if (legacy?.Positions != null) {
                            var tracker = new PlaybackPositionTracker();
                            foreach (var kvp in legacy.Positions) {
                                tracker.Positions[kvp.Key] = new FilePositionData {
                                    FilePath = kvp.Key,
                                    Position = kvp.Value,
                                    LastUpdated = DateTime.Now
                                };
                            }
                            tracker.SaveSync();
                            try { File.Delete(legacyPath); } catch { }
                            return tracker;
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.Error("[PositionTracker] Critical failure loading playback positions", ex);
            }
            return new PlaybackPositionTracker();
        }

        private class LegacyTracker {
            public Dictionary<string, long> Positions { get; set; }
        }

        public void UpdatePosition(string filePath, TimeSpan position) {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            
            if (App.Settings?.RememberFilePosition != true) {
                return;
            }
            
            // Only track if position is significant (> 5 seconds)
            if (position.TotalSeconds < 5) {
                return;
            }

            var key = GetFileKey(filePath);
            lock (Positions) {
                if (Positions.TryGetValue(key, out var existing)) {
                    // Only update if the new position is actually different (avoid redundant writes)
                    var diff = Math.Abs(existing.Position - position.Ticks);
                    if (diff < TimeSpan.FromSeconds(1).Ticks) return; 
                }

                Positions[key] = new FilePositionData {
                    FilePath = filePath,
                    Position = position.Ticks,
                    LastUpdated = DateTime.Now
                };
            }
            
            Logger.Debug($"[PositionTracker] Updated position for {Path.GetFileName(filePath)} to {position:mm\\:ss}");

            // Start timer if not running to batch saves
            if (_saveTimer != null && !_saveTimer.IsEnabled) {
                _saveTimer.Start();
            }
        }

        public TimeSpan? GetPosition(string filePath) {
            if (string.IsNullOrWhiteSpace(filePath)) return null;
            
            if (App.Settings?.RememberFilePosition != true) {
                Logger.Info($"[PositionTracker] RememberFilePosition setting is OFF. Skipping position lookup for {Path.GetFileName(filePath)}.");
                return null;
            }

            var key = GetFileKey(filePath);
            lock (Positions) {
                if (Positions.TryGetValue(key, out var data)) {
                    var pos = TimeSpan.FromTicks(data.Position);
                    Logger.Info($"[PositionTracker] Found saved position for {Path.GetFileName(filePath)}: {pos:mm\\:ss}");
                    return pos;
                }
            }
            Logger.Info($"[PositionTracker] No saved position for {Path.GetFileName(filePath)}");
            return null;
        }
        
        public void ClearPosition(string filePath) {
             var key = GetFileKey(filePath);
             lock (Positions) {
                 if (Positions.Remove(key)) {
                     Logger.Info($"[PositionTracker] Cleared position for {Path.GetFileName(filePath)} (video finished)");
                     _ = SaveAsync();
                 }
             }
        }

        public void ClearAllPositions() {
            lock (Positions) {
                Positions.Clear();
            }
            Logger.Info("[PositionTracker] Cleared all saved positions");
            _ = SaveAsync();
        }

        private string GetFileKey(string filePath) {
            if (string.IsNullOrWhiteSpace(filePath)) return string.Empty;

            // Fast return from cache
            if (_pathKeyCache.TryGetValue(filePath, out var cachedKey)) {
                return cachedKey;
            }

            string key;
            try {
                // Check if it's a URL
                if (filePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                    filePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
                    
                    // For URLs, we don't use Path.GetFullPath. 
                    // We just lower it and trim query parameters that look like tokens
                    var normalized = filePath.Trim();
                    
                    // Improved normalization: Remove common expiring token parameters
                    // This acts as a fallback if OriginalPageUrl wasn't correctly preserved
                    try {
                        if (normalized.Contains('?')) {
                            // Regex to remove common token parameters (token, expires, sig, expire, key, etc.)
                            // Matches ?param=value or &param=value
                            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, 
                                @"([?&])(token|expires|sig|expire|key|st|e)=[^&]*", 
                                "", 
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                            
                            // Cleanup any dangling ? or & at the end, or double &&
                            normalized = normalized.TrimEnd('?', '&');
                            normalized = normalized.Replace("&&", "&");
                            
                            // If the query string is now empty or just '?', remove it?
                            // For now, let's just ensure we don't have a trailing ?
                            if (normalized.EndsWith("?")) normalized = normalized.Substring(0, normalized.Length - 1);
                        }
                    } catch (Exception ex) {
                        Logger.Warning($"[PositionTracker] Regex normalization failed for '{filePath}': {ex.Message}");
                    }

                    key = normalized.ToLowerInvariant();
                } else {
                    // For local files, resolve full path and lowercase
                    key = Path.GetFullPath(filePath).ToLowerInvariant();
                }
            } catch (Exception ex) {
                Logger.Warning($"[PositionTracker] Failed to normalize path '{filePath}': {ex.Message}");
                key = filePath.Trim().ToLowerInvariant();
            }
            
            Logger.Debug($"[PositionTracker] Key for '{filePath}' -> '{key}'");

            // Store in cache
            _pathKeyCache.TryAdd(filePath, key);
            return key;
        }

        /// <summary>
        /// Cleans up entries older than the specified number of days
        /// </summary>
        public void CleanupOldPositions(int daysToKeep = 30) {
            var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
            int removedCount = 0;

            lock (Positions) {
                var keysToRemove = Positions.Where(kvp => kvp.Value.LastUpdated < cutoffDate)
                                          .Select(kvp => kvp.Key)
                                          .ToList();

                foreach (var key in keysToRemove) {
                    Positions.Remove(key);
                }
                removedCount = keysToRemove.Count;
            }

            if (removedCount > 0) {
                Logger.Info($"[PositionTracker] Cleaned up {removedCount} old sessions (older than {daysToKeep} days)");
                _ = SaveAsync();
            }
        }

        /// <summary>
        /// Asynchronously saves positions to disk
        /// </summary>
        public async Task SaveAsync() {
            if (App.Settings?.RememberFilePosition != true && Positions.Count == 0) return;

            await _saveLock.WaitAsync();
            try {
                DoSave();
            } catch (Exception ex) {
                Logger.Error("[PositionTracker] Failed to save playback positions (async)", ex);
            } finally {
                _saveLock.Release();
            }
        }

        /// <summary>
        /// Synchronously saves positions to disk. Use during application shutdown.
        /// </summary>
        public void SaveSync() {
            _saveLock.Wait();
            try {
                DoSave();
            } catch (Exception ex) {
                Logger.Error("[PositionTracker] Failed to save playback positions (sync)", ex);
            } finally {
                _saveLock.Release();
            }
        }

        private void DoSave() {
            string json;
            int count;
            lock (Positions) {
                count = Positions.Count;
                json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            }
            
            try {
                var directory = Path.GetDirectoryName(_settingsPath);
                if (!Directory.Exists(directory)) {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(_settingsPath, json);
                Logger.Info($"[PositionTracker] Successfully saved {count} positions to data directory");
            } catch (Exception ex) {
                Logger.Error("[PositionTracker] Critical error during file write", ex);
            }
            
            if (_saveTimer != null && _saveTimer.IsEnabled) {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() => _saveTimer.Stop());
            }
        }

        private void Save() {
            _ = SaveAsync();
        }
    }
}
