using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MultilayerCache.Cache
{
    public class InMemoryCache<TKey, TValue> : ICache<TKey, TValue>, IDisposable
     where TKey: notnull
    {
        private readonly ConcurrentDictionary<TKey, CacheItem<TValue>> _cache = new();
        private readonly Timer _cleanupTimer;
        private readonly ILogger _logger;

        public CacheMetrics Metrics { get; } = new();

        public InMemoryCache(TimeSpan cleanupInterval, ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cleanupTimer = new Timer(_ => Cleanup(), null, cleanupInterval, cleanupInterval);
        }

        public void Set(TKey key, TValue value, TimeSpan ttl)
        {
            _cache[key] = new CacheItem<TValue>(value, ttl);
            _logger.LogDebug("Set key {Key} with TTL {TTL}", key, ttl);
        }

        public bool TryGet(TKey key, out TValue value)
        {
            if (_cache.TryGetValue(key, out var item))
            {
                if (!item.IsExpired)
                {
                    Metrics.IncrementHit();
                    _logger.LogDebug("Cache hit for key {Key}", key);
                    value = item.Value;
                    return true;
                }

                _cache.TryRemove(key, out _);
                _logger.LogDebug("Cache expired for key {Key}, removed", key);
            }

            Metrics.IncrementMiss();
            _logger.LogDebug("Cache miss for key {Key}", key);
            value = default!;
            return false;
        }

        private void Cleanup()
        {
            foreach (var kvp in _cache)
            {
                if (kvp.Value.IsExpired)
                {
                    _cache.TryRemove(kvp.Key, out _);
                    _logger.LogDebug("Cleanup removed expired key {Key}", kvp.Key);
                }
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
        }

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
