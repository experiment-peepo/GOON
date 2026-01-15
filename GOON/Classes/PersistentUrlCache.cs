using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace GOON.Classes {
    /// <summary>
    /// Represents a cached URL entry with expiration
    /// </summary>
    public class UrlCacheEntry {
        public string ResolvedUrl { get; set; }
        public DateTime ExpireTime { get; set; }
        public DateTime CreatedTime { get; set; }

        [JsonIgnore]
        public bool IsExpired => DateTime.UtcNow > ExpireTime;
    }

    /// <summary>
    /// Handles persistent caching of resolved video URLs with expiration awareness
    /// </summary>
    public class PersistentUrlCache : IDisposable {
        private const string CacheFileName = "url_cache.json";
        private readonly string _cacheFilePath;
        private ConcurrentDictionary<string, UrlCacheEntry> _cache;
        private readonly System.Threading.Timer _saveTimer;
        private bool _isDirty;
        private readonly object _lock = new object();

        private static PersistentUrlCache _instance;
        public static PersistentUrlCache Instance => _instance ??= new PersistentUrlCache();

        public PersistentUrlCache() {
            _cacheFilePath = Path.Combine(AppPaths.DataDirectory, CacheFileName);
            _cache = new ConcurrentDictionary<string, UrlCacheEntry>();
            LoadCache();

            // Auto-save every 30 seconds if dirty
            _saveTimer = new System.Threading.Timer(SaveCallback, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        private void LoadCache() {
            try {
                if (File.Exists(_cacheFilePath)) {
                    var json = File.ReadAllText(_cacheFilePath);
                    var dictionary = JsonSerializer.Deserialize<Dictionary<string, UrlCacheEntry>>(json);
                    if (dictionary != null) {
                        // Filter out already expired entries during load
                        var validEntries = dictionary.Where(kvp => !kvp.Value.IsExpired);
                        _cache = new ConcurrentDictionary<string, UrlCacheEntry>(validEntries);
                        Logger.Info($"[PersistentUrlCache] Loaded {_cache.Count} valid entries");
                    }
                }
            } catch (Exception ex) {
                Logger.Warning($"[PersistentUrlCache] Failed to load cache: {ex.Message}");
                // Start fresh if corrupt
                _cache = new ConcurrentDictionary<string, UrlCacheEntry>();
            }
        }

        private void SaveCallback(object state) {
            if (_isDirty) {
                SaveCache();
            }
        }

        public void SaveCache() {
            lock (_lock) {
                try {
                    if (!_isDirty && File.Exists(_cacheFilePath)) return;

                    // Clean up expired entries before saving
                    var cleanCache = _cache.Where(kvp => !kvp.Value.IsExpired).ToDictionary(k => k.Key, v => v.Value);
                    
                    var json = JsonSerializer.Serialize(cleanCache);
                    File.WriteAllText(_cacheFilePath, json);
                    _isDirty = false;
                    // Update in-memory cache to match cleaned version (optional, but good for memory)
                    _cache = new ConcurrentDictionary<string, UrlCacheEntry>(cleanCache);
                } catch (Exception ex) {
                    Logger.Warning($"[PersistentUrlCache] Failed to save cache: {ex.Message}");
                }
            }
        }

        public string Get(string pageUrl) {
            if (_cache.TryGetValue(pageUrl, out var entry)) {
                if (!entry.IsExpired) {
                    return entry.ResolvedUrl;
                } else {
                    // Lazy remove
                    _cache.TryRemove(pageUrl, out _);
                    _isDirty = true;
                }
            }
            return null;
        }

        public void Set(string pageUrl, string resolvedUrl, DateTime? expireTime = null) {
            if (string.IsNullOrEmpty(pageUrl) || string.IsNullOrEmpty(resolvedUrl)) return;

            // If no expiration provided, try to parse it from URL, or default to 60 mins
            if (!expireTime.HasValue) {
                expireTime = ParseExpiration(resolvedUrl) ?? DateTime.UtcNow.AddMinutes(60);
            }

            // Don't cache if already expired
            if (expireTime.Value <= DateTime.UtcNow) return;

            var entry = new UrlCacheEntry {
                ResolvedUrl = resolvedUrl,
                ExpireTime = expireTime.Value,
                CreatedTime = DateTime.UtcNow
            };

            _cache.AddOrUpdate(pageUrl, entry, (k, v) => entry);
            _isDirty = true;
        }

        /// <summary>
        /// Parses expiration timestamp from URL parameters like 'expire', 'expires', 'ttl'
        /// </summary>
        private DateTime? ParseExpiration(string url) {
            try {
                if (string.IsNullOrEmpty(url)) return null;

                var uri = new Uri(url);
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

                // Common parameters for expiration
                string[] paramNames = { "expire", "expires", "ttl", "time", "t" };
                
                foreach (var param in paramNames) {
                    var value = query[param];
                    if (!string.IsNullOrEmpty(value) && long.TryParse(value, out long timestamp)) {
                        DateTime expiration;
                        if (timestamp > 253402300799) { // Roughly year 9999 in seconds, so likely ms
                             expiration = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;
                        } else {
                             expiration = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
                        }
                        return expiration;
                    }
                }
            } catch {
                // Ignore parsing errors
            }
            return null;
        }

        public void Remove(string pageUrl) {
            if (string.IsNullOrEmpty(pageUrl)) return;
            if (_cache.TryRemove(pageUrl, out _)) {
                _isDirty = true;
            }
        }

        public void Clear() {
            _cache.Clear();
            _isDirty = true;
            SaveCache();
        }

        public void Dispose() {
            _saveTimer?.Dispose();
            SaveCache();
        }
    }
}
