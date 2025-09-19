using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace MultilayerCache.Cache
{
    public class InMemoryCache<TKey, TValue> : ICache<TKey, TValue>, IDisposable
    {
        private readonly ConcurrentDictionary<TKey, CacheItem<TValue>> _cache = new();
        private readonly Timer _cleanupTimer;

        public CacheMetrics Metrics { get; } = new();

        public InMemoryCache(TimeSpan cleanupInterval)
        {
            _cleanupTimer = new Timer(_ => Cleanup(), null, cleanupInterval, cleanupInterval);
        }

        public void Set(TKey key, TValue value, TimeSpan ttl)
        {
            _cache[key] = new CacheItem<TValue>(value, ttl);
        }

        public bool TryGet(TKey key, out TValue value)
        {
            if (_cache.TryGetValue(key, out var item))
            {
                if (!item.IsExpired)
                {
                    Metrics.IncrementHit();
                    value = item.Value;
                    return true;
                }
                _cache.TryRemove(key, out _);
            }

            Metrics.IncrementMiss();
            value = default;
            return false;
        }

        private void Cleanup()
        {
            foreach (var kvp in _cache)
                if (kvp.Value.IsExpired)
                    _cache.TryRemove(kvp.Key, out _);
        }

        public void Dispose() => _cleanupTimer?.Dispose();

        public Task SetAsync(TKey key, TValue value, TimeSpan ttl)
        {
            Set(key, value, ttl);
            return Task.CompletedTask;
        }

        public Task<(bool found, TValue value)> TryGetAsync(TKey key)
        {
            var found = TryGet(key, out var value);
            return Task.FromResult((found, value));
        }
    }
}
