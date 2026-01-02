using System;
using System.Collections.Generic;

namespace TrainMeX.Classes {
    /// <summary>
    /// LRU (Least Recently Used) cache with size limit and TTL support
    /// </summary>
    public class LruCache<TKey, TValue> {
        private readonly int _maxSize;
        private readonly TimeSpan? _ttl;
        private readonly Dictionary<TKey, CacheEntry> _cache;
        private readonly LinkedList<TKey> _accessOrder;
        private readonly object _lock = new object();

        private class CacheEntry {
            public TValue Value { get; set; }
            public DateTime CreatedAt { get; set; }
            public LinkedListNode<TKey> Node { get; set; }
        }

        /// <summary>
        /// Creates a new LRU cache
        /// </summary>
        /// <param name="maxSize">Maximum number of entries</param>
        /// <param name="ttl">Time to live for entries (null for no expiration)</param>
        public LruCache(int maxSize, TimeSpan? ttl = null) {
            _maxSize = maxSize;
            _ttl = ttl;
            _cache = new Dictionary<TKey, CacheEntry>();
            _accessOrder = new LinkedList<TKey>();
        }

        /// <summary>
        /// Gets a value from the cache
        /// </summary>
        public bool TryGetValue(TKey key, out TValue value) {
            value = default(TValue);
            
            lock (_lock) {
                if (!_cache.TryGetValue(key, out var entry)) {
                    return false;
                }

                // Check if entry has expired
                if (_ttl.HasValue && DateTime.Now - entry.CreatedAt > _ttl.Value) {
                    RemoveInternal(key);
                    return false;
                }

                // Move to front (most recently used)
                _accessOrder.Remove(entry.Node);
                entry.Node = _accessOrder.AddFirst(key);
                
                value = entry.Value;
                return true;
            }
        }

        /// <summary>
        /// Adds or updates a value in the cache
        /// </summary>
        public void Set(TKey key, TValue value) {
            lock (_lock) {
                if (_cache.TryGetValue(key, out var existingEntry)) {
                    // Update existing entry
                    existingEntry.Value = value;
                    existingEntry.CreatedAt = DateTime.Now;
                    _accessOrder.Remove(existingEntry.Node);
                    existingEntry.Node = _accessOrder.AddFirst(key);
                    return;
                }

                // Remove least recently used if at capacity
                if (_cache.Count >= _maxSize) {
                    var lruKey = _accessOrder.Last.Value;
                    RemoveInternal(lruKey);
                }

                // Add new entry
                var node = _accessOrder.AddFirst(key);
                _cache[key] = new CacheEntry {
                    Value = value,
                    CreatedAt = DateTime.Now,
                    Node = node
                };
            }
        }

        /// <summary>
        /// Removes an entry from the cache (internal version without locking, assumes lock is held)
        /// </summary>
        private void RemoveInternal(TKey key) {
            if (_cache.TryGetValue(key, out var entry)) {
                _accessOrder.Remove(entry.Node);
                _cache.Remove(key);
            }
        }

        /// <summary>
        /// Removes an entry from the cache
        /// </summary>
        public void Remove(TKey key) {
            lock (_lock) {
                RemoveInternal(key);
            }
        }

        /// <summary>
        /// Clears all entries from the cache
        /// </summary>
        public void Clear() {
            lock (_lock) {
                _cache.Clear();
                _accessOrder.Clear();
            }
        }

        /// <summary>
        /// Gets the number of entries in the cache
        /// </summary>
        public int Count {
            get {
                lock (_lock) {
                    return _cache.Count;
                }
            }
        }
    }
}


