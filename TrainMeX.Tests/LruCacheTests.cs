using System;
using System.Threading;
using TrainMeX.Classes;
using Xunit;

namespace TrainMeX.Tests {
    public class LruCacheTests {
        [Fact]
        public void Constructor_WithMaxSize_SetsCapacity() {
            var cache = new LruCache<string, int>(10);
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void Set_AddsNewEntry() {
            var cache = new LruCache<string, int>(10);
            cache.Set("key1", 100);
            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public void TryGetValue_WithExistingKey_ReturnsTrueAndValue() {
            var cache = new LruCache<string, int>(10);
            cache.Set("key1", 100);
            
            var result = cache.TryGetValue("key1", out int value);
            
            Assert.True(result);
            Assert.Equal(100, value);
        }

        [Fact]
        public void TryGetValue_WithNonExistentKey_ReturnsFalse() {
            var cache = new LruCache<string, int>(10);
            
            var result = cache.TryGetValue("nonexistent", out int value);
            
            Assert.False(result);
            Assert.Equal(default(int), value);
        }

        [Fact]
        public void Set_UpdatesExistingEntry() {
            var cache = new LruCache<string, int>(10);
            cache.Set("key1", 100);
            cache.Set("key1", 200);
            
            var result = cache.TryGetValue("key1", out int value);
            
            Assert.True(result);
            Assert.Equal(200, value);
            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public void Set_WhenAtCapacity_EvictsLeastRecentlyUsed() {
            var cache = new LruCache<string, int>(3);
            
            cache.Set("key1", 1);
            cache.Set("key2", 2);
            cache.Set("key3", 3);
            
            // Access key1 and key2 to make key3 the LRU
            cache.TryGetValue("key1", out _);
            cache.TryGetValue("key2", out _);
            
            // Add new entry - should evict key3
            cache.Set("key4", 4);
            
            Assert.Equal(3, cache.Count);
            Assert.False(cache.TryGetValue("key3", out _));
            Assert.True(cache.TryGetValue("key1", out _));
            Assert.True(cache.TryGetValue("key2", out _));
            Assert.True(cache.TryGetValue("key4", out _));
        }

        [Fact]
        public void Set_WhenAtCapacity_EvictsInCorrectOrder() {
            var cache = new LruCache<string, int>(3);
            
            cache.Set("key1", 1);
            cache.Set("key2", 2);
            cache.Set("key3", 3);
            
            // key1 is now LRU (first added, never accessed)
            cache.Set("key4", 4);
            
            Assert.False(cache.TryGetValue("key1", out _));
            Assert.True(cache.TryGetValue("key2", out _));
            Assert.True(cache.TryGetValue("key3", out _));
            Assert.True(cache.TryGetValue("key4", out _));
        }

        [Fact]
        public void Remove_RemovesEntry() {
            var cache = new LruCache<string, int>(10);
            cache.Set("key1", 100);
            cache.Remove("key1");
            
            Assert.Equal(0, cache.Count);
            Assert.False(cache.TryGetValue("key1", out _));
        }

        [Fact]
        public void Remove_WithNonExistentKey_DoesNothing() {
            var cache = new LruCache<string, int>(10);
            cache.Set("key1", 100);
            cache.Remove("nonexistent");
            
            Assert.Equal(1, cache.Count);
            Assert.True(cache.TryGetValue("key1", out _));
        }

        [Fact]
        public void Clear_RemovesAllEntries() {
            var cache = new LruCache<string, int>(10);
            cache.Set("key1", 1);
            cache.Set("key2", 2);
            cache.Set("key3", 3);
            
            cache.Clear();
            
            Assert.Equal(0, cache.Count);
            Assert.False(cache.TryGetValue("key1", out _));
            Assert.False(cache.TryGetValue("key2", out _));
            Assert.False(cache.TryGetValue("key3", out _));
        }

        [Fact]
        public void TryGetValue_WithExpiredEntry_ReturnsFalse() {
            var cache = new LruCache<string, int>(10, TimeSpan.FromMilliseconds(100));
            cache.Set("key1", 100);
            
            Thread.Sleep(150);
            
            var result = cache.TryGetValue("key1", out int value);
            
            Assert.False(result);
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void TryGetValue_WithNonExpiredEntry_ReturnsTrue() {
            var cache = new LruCache<string, int>(10, TimeSpan.FromSeconds(1));
            cache.Set("key1", 100);
            
            var result = cache.TryGetValue("key1", out int value);
            
            Assert.True(result);
            Assert.Equal(100, value);
        }

        [Fact]
        public void Set_WithNullTtl_DoesNotExpire() {
            var cache = new LruCache<string, int>(10, null);
            cache.Set("key1", 100);
            
            Thread.Sleep(100);
            
            var result = cache.TryGetValue("key1", out int value);
            
            Assert.True(result);
            Assert.Equal(100, value);
        }

        [Fact]
        public void TryGetValue_UpdatesAccessOrder() {
            var cache = new LruCache<string, int>(3);
            
            cache.Set("key1", 1);
            cache.Set("key2", 2);
            cache.Set("key3", 3);
            
            // Access key1 to make it most recently used
            cache.TryGetValue("key1", out _);
            
            // Add new entry - should evict key2 (LRU, not key1)
            cache.Set("key4", 4);
            
            Assert.True(cache.TryGetValue("key1", out _));
            Assert.False(cache.TryGetValue("key2", out _));
            Assert.True(cache.TryGetValue("key3", out _));
            Assert.True(cache.TryGetValue("key4", out _));
        }

        [Fact]
        public void Set_WithUpdate_ResetsExpirationTime() {
            var cache = new LruCache<string, int>(10, TimeSpan.FromMilliseconds(200));
            cache.Set("key1", 100);
            
            Thread.Sleep(150);
            
            // Update should reset expiration
            cache.Set("key1", 200);
            
            Thread.Sleep(100);
            
            // Should still be valid
            var result = cache.TryGetValue("key1", out int value);
            Assert.True(result);
            Assert.Equal(200, value);
        }

        [Fact]
        public void Count_ReflectsCurrentEntries() {
            var cache = new LruCache<string, int>(10);
            Assert.Equal(0, cache.Count);
            
            cache.Set("key1", 1);
            Assert.Equal(1, cache.Count);
            
            cache.Set("key2", 2);
            Assert.Equal(2, cache.Count);
            
            cache.Remove("key1");
            Assert.Equal(1, cache.Count);
        }
    }
}

