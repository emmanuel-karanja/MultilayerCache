using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MultilayerCache.Cache
{
    /// <summary>
    /// Enhanced in-memory cache implementing W-TinyLFU admission, early refresh tracking,
    /// per-key metrics, L1 promotion support, and eviction with Count-Min Sketch.
    /// </summary>
    public class EnhancedWTinyLFUInMemoryCache<TKey, TValue> : ICache<TKey, TValue>, IDisposable
        where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, CacheItem<TValue>> _cache = new();
        private readonly Timer _cleanupTimer;
        private readonly ILogger _logger;

        private readonly CountMinSketch<TKey> _frequencyTracker;
        private readonly BloomFilter<TKey> _bloomFilter;
        private readonly Random _rand = new();
        private readonly int _maxSize;
        private readonly bool _useTinyLFU;
        private readonly TimeSpan _earlyRefreshThreshold;
        private readonly Timer _decayTimer;
        private readonly TimeSpan _decayInterval;

        // Metrics
        private readonly ConcurrentDictionary<TKey, int> _hitsPerKey = new();
        private readonly ConcurrentDictionary<TKey, int> _missesPerKey = new();
        private readonly ConcurrentDictionary<TKey, double> _lastLatencyPerKey = new();
        private readonly ConcurrentDictionary<TKey, int> _earlyRefreshCountPerKey = new();
        private readonly ConcurrentDictionary<TKey, int> _promotionCountPerKey = new();
        private readonly ConcurrentDictionary<TKey, DateTime> _lastRefreshTimestamp = new();
        private readonly ConcurrentDictionary<TKey, bool> _inFlightKeys = new();

        /// <summary>
        /// Constructor
        /// </summary>
        public EnhancedWTinyLFUInMemoryCache(
            TimeSpan cleanupInterval,
            ILogger logger,
            int maxSize = 1000,
            bool useTinyLFU = true,
            TimeSpan? decayInterval = null,
            TimeSpan? earlyRefreshThreshold = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cleanupTimer = new Timer(_ => Cleanup(), null, cleanupInterval, cleanupInterval);

            _frequencyTracker = new CountMinSketch<TKey>(1000, 5);
            _bloomFilter = new BloomFilter<TKey>(maxSize * 2, 5); // double size for better cold key tracking
            _maxSize = maxSize;
            _useTinyLFU = useTinyLFU;

            _earlyRefreshThreshold = earlyRefreshThreshold ?? TimeSpan.FromSeconds(30);
            _decayInterval = decayInterval ?? TimeSpan.FromMinutes(5);
            _decayTimer = new Timer(_ => _frequencyTracker.Decay(), null, _decayInterval, _decayInterval);
        }

        /// <summary>
        /// Set a value in cache with TTL using W-TinyLFU admission and optional eviction.
        /// </summary>
        public void Set(TKey key, TValue value, TimeSpan ttl)
        {
            var sw = Stopwatch.StartNew();
            _frequencyTracker.Increment(key);

            if (_useTinyLFU)
            {
                // Check if this key is a cold key
                bool isCold = !_bloomFilter.Contains(key);
                if (isCold)
                {
                    _bloomFilter.Add(key);

                    // Estimate admission probability based on frequency vs victim
                    long freqNew = _frequencyTracker.Estimate(key);
                    long freqVictim = _cache.Count > 0 ? IdentifyVictimFrequency() : 0;
                    double admitProb = freqNew / (double)(freqNew + freqVictim + 1);

                    if (_rand.NextDouble() > admitProb)
                    {
                        _logger.LogDebug("W-TinyLFU rejected cold key {Key} with probability {Prob}", key, 1 - admitProb);
                        return; // reject key
                    }
                }

                // Evict victim if cache full
                if (_cache.Count >= _maxSize)
                {
                    var victim = IdentifyVictim();
                    if (_frequencyTracker.Estimate(key) < _frequencyTracker.Estimate(victim))
                    {
                        _logger.LogDebug("TinyLFU rejected key {Key} due to low frequency", key);
                        return;
                    }
                    _cache.TryRemove(victim, out _);
                }
            }

            // Add/replace in cache
            _cache[key] = new CacheItem<TValue>(value, ttl);
            _lastRefreshTimestamp[key] = DateTime.UtcNow;
            _promotionCountPerKey.AddOrUpdate(key, 1, (_, v) => v + 1);

            sw.Stop();
            _lastLatencyPerKey[key] = sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Try to get a value from cache. Updates frequency, metrics, and early refresh counters.
        /// </summary>
        public bool TryGet(TKey key, out TValue value)
        {
            var sw = Stopwatch.StartNew();
            _frequencyTracker.Increment(key);

            if (_cache.TryGetValue(key, out var item))
            {
                if (!item.IsExpired)
                {
                    _hitsPerKey.AddOrUpdate(key, 1, (_, v) => v + 1);

                    // Track early refresh if TTL close
                    if (item.ExpiryTime - DateTime.UtcNow <= _earlyRefreshThreshold)
                        _earlyRefreshCountPerKey.AddOrUpdate(key, 1, (_, v) => v + 1);

                    value = item.Value;
                    sw.Stop();
                    _lastLatencyPerKey[key] = sw.Elapsed.TotalMilliseconds;
                    return true;
                }
                _cache.TryRemove(key, out _);
            }

            _missesPerKey.AddOrUpdate(key, 1, (_, v) => v + 1);
            value = default!;
            sw.Stop();
            _lastLatencyPerKey[key] = sw.Elapsed.TotalMilliseconds;
            return false;
        }

        /// <summary>
        /// Promote a value from lower cache layer to this cache (L1).
        /// </summary>
        public void PromoteFromLowerLayer(TKey key, TValue value, TimeSpan remainingTtl)
        {
            _cache[key] = new CacheItem<TValue>(value, remainingTtl);
            _promotionCountPerKey.AddOrUpdate(key, 1, (_, v) => v + 1);
        }

        /// <summary>
        /// Identify victim key for eviction using TinyLFU sampling.
        /// </summary>
        private TKey IdentifyVictim()
        {
            var sample = _cache.Keys.OrderBy(_ => Guid.NewGuid()).Take(5);
            TKey victim = default!;
            long minFreq = long.MaxValue;

            foreach (var key in sample)
            {
                long freq = _frequencyTracker.Estimate(key);
                if (freq < minFreq)
                {
                    minFreq = freq;
                    victim = key;
                }
            }

            return victim;
        }

        /// <summary>
        /// Frequency of sampled victim for soft admission probability.
        /// </summary>
        private long IdentifyVictimFrequency()
        {
            var sample = _cache.Keys.OrderBy(_ => Guid.NewGuid()).Take(5);
            long minFreq = long.MaxValue;
            foreach (var key in sample)
            {
                long freq = _frequencyTracker.Estimate(key);
                if (freq < minFreq) minFreq = freq;
            }
            return minFreq;
        }

        /// <summary>
        /// Cleanup expired cache items.
        /// </summary>
        private void Cleanup()
        {
            foreach (var kvp in _cache)
                if (kvp.Value.IsExpired)
                    _cache.TryRemove(kvp.Key, out _);
        }

        /// <summary>
        /// Returns a snapshot of metrics for monitoring or analysis.
        /// </summary>
        public CacheMetricsSnapshot<TKey> GetMetricsSnapshot()
        {
            return new CacheMetricsSnapshot<TKey>
            {
                HitsPerKey = new(_hitsPerKey),
                MissesPerKey = new(_missesPerKey),
                LastLatencyPerKey = new(_lastLatencyPerKey),
                LastRefreshTimestamp = new(_lastRefreshTimestamp),
                EarlyRefreshCountPerKey = new(_earlyRefreshCountPerKey),
                PromotionCountPerKey = new(_promotionCountPerKey),
                InFlightKeys = new(_inFlightKeys.Keys),
                TotalHits = _hitsPerKey.Values.Sum(),
                TotalMisses = _missesPerKey.Values.Sum(),
                TotalPromotions = _promotionCountPerKey.Values.Sum(),
                TotalEarlyRefreshes = _earlyRefreshCountPerKey.Values.Sum(),
                TopKeysByAccessCount = _hitsPerKey.OrderByDescending(kv => kv.Value).Take(10).Select(kv => kv.Key).ToArray()
            };
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

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _decayTimer?.Dispose();
        }
    }

    #region Supporting Classes

    /// <summary>
    /// Count-Min Sketch: estimates frequency of keys for TinyLFU eviction
    /// </summary>
    public class CountMinSketch<TKey>
    {
        private readonly int _width;
        private readonly int _depth;
        private readonly long[,] _table;
        private readonly int[] _hashSeeds;
        private readonly Random _rand = new();
        private readonly object _lock = new();

        public CountMinSketch(int width, int depth)
        {
            _width = width;
            _depth = depth;
            _table = new long[depth, width];
            _hashSeeds = Enumerable.Range(0, depth).Select(_ => _rand.Next()).ToArray();
        }

        public void Increment(TKey key)
        {
            for (int i = 0; i < _depth; i++)
            {
                int index = (MixHash(key.GetHashCode(), _hashSeeds[i]) % _width + _width) % _width;
                Interlocked.Increment(ref _table[i, index]);
            }
        }

        public long Estimate(TKey key)
        {
            long min = long.MaxValue;
            for (int i = 0; i < _depth; i++)
            {
                int index = (MixHash(key.GetHashCode(), _hashSeeds[i]) % _width + _width) % _width;
                min = Math.Min(min, Interlocked.Read(ref _table[i, index]));
            }
            return min;
        }

        public void Decay()
        {
            lock (_lock)
            {
                for (int i = 0; i < _depth; i++)
                    for (int j = 0; j < _width; j++)
                        _table[i, j] /= 2;
            }
        }

        private static int MixHash(int hash, int seed) => hash ^ seed;
    }

    /// <summary>
    /// Simple Bloom filter for cold-key tracking in W-TinyLFU
    /// </summary>
    public class BloomFilter<TKey>
    {
        private readonly bool[] _bits;
        private readonly int[] _hashSeeds;
        private readonly Random _rand = new();

        public BloomFilter(int size, int hashCount)
        {
            _bits = new bool[size];
            _hashSeeds = Enumerable.Range(0, hashCount).Select(_ => _rand.Next()).ToArray();
        }

        public void Add(TKey key)
        {
            foreach (var seed in _hashSeeds)
            {
                int index = (MixHash(key.GetHashCode(), seed) % _bits.Length + _bits.Length) % _bits.Length;
                _bits[index] = true;
            }
        }

        public bool Contains(TKey key)
        {
            foreach (var seed in _hashSeeds)
            {
                int index = (MixHash(key.GetHashCode(), seed) % _bits.Length + _bits.Length) % _bits.Length;
                if (!_bits[index]) return false;
            }
            return true;
        }

        private static int MixHash(int hash, int seed) => hash ^ seed;
    }

    #endregion
}
