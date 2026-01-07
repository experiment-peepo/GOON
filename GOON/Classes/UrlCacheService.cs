using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GOON.Classes {
    public class UrlCacheEntry {
        public string DirectUrl { get; set; }
        public DateTime Expiry { get; set; }
    }

    public class UrlCacheService {
        private readonly string _cachePath;
        private readonly Dictionary<string, UrlCacheEntry> _cache;
        private readonly object _lock = new object();

        public UrlCacheService() {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _cachePath = Path.Combine(appData, "GOON", "urlcache.json");
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath));
            _cache = LoadCache();
            CleanupExpired();
        }

        private Dictionary<string, UrlCacheEntry> LoadCache() {
            try {
                if (File.Exists(_cachePath)) {
                    string json = File.ReadAllText(_cachePath);
                    return JsonSerializer.Deserialize<Dictionary<string, UrlCacheEntry>>(json) ?? new Dictionary<string, UrlCacheEntry>();
                }
            } catch (Exception ex) {
                Logger.Warning("Failed to load URL cache", ex);
            }
            return new Dictionary<string, UrlCacheEntry>();
        }

        public void SaveCache() {
            lock (_lock) {
                try {
                    string json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_cachePath, json);
                } catch (Exception ex) {
                    Logger.Warning("Failed to save URL cache", ex);
                }
            }
        }

        public bool TryGetValue(string pageUrl, out string directUrl) {
            lock (_lock) {
                if (_cache.TryGetValue(pageUrl, out var entry)) {
                    if (entry.Expiry > DateTime.Now) {
                        directUrl = entry.DirectUrl;
                        return true;
                    } else {
                        _cache.Remove(pageUrl);
                    }
                }
            }
            directUrl = null;
            return false;
        }

        public void Set(string pageUrl, string directUrl, TimeSpan ttl) {
            lock (_lock) {
                _cache[pageUrl] = new UrlCacheEntry {
                    DirectUrl = directUrl,
                    Expiry = DateTime.Now.Add(ttl)
                };
            }
        }

        private void CleanupExpired() {
            lock (_lock) {
                var now = DateTime.Now;
                var expiredKeys = _cache.Where(kvp => kvp.Value.Expiry <= now).Select(kvp => kvp.Key).ToList();
                foreach (var key in expiredKeys) {
                    _cache.Remove(key);
                }
            }
        }

        public void ClearCache() {
            lock (_lock) {
                _cache.Clear();
            }
        }
    }
}
