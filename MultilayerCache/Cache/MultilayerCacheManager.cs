using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MultilayerCache.Cache
{
    /// <summary>
    /// Policy for promoting values from lower cache layers to higher ones on a cache hit.
    /// </summary>
    public enum PromotionPolicy
    {
        /// <summary>Promote value to all higher layers.</summary>
        AllLayers,
        /// <summary>Promote value only to the first layer.</summary>
        FirstLayerOnly,
        /// <summary>No promotion on cache hit.</summary>
        None
    }

    /// <summary>
    /// Manages multi-layer caching with the following features:
    /// 
    /// 1. Multi-layer caching: Supports an array of cache layers (e.g., in-memory, Redis)
    ///    with automatic promotion of frequently accessed items to higher (faster) layers.
    /// 
    /// 2. Cache miss handling with request coalescing (Thundering Herd mitigation):
    ///    - Tracks in-flight loader tasks per key using Lazy<Task> to prevent multiple concurrent loads for the same key.
    ///    - Optionally uses per-key semaphores for ultra-hot keys to further prevent contention.
    /// 
    /// 3. Write policies: Supports pluggable write strategies (WriteThrough, WriteBehind, etc.)
    ///    for propagating values to cache layers and persistent storage.
    /// 
    /// 4. Early refresh (soft TTL):
    ///    - Automatically triggers background refresh of cached items approaching TTL.
    ///    - Includes per-key throttling (_minRefreshInterval) and global concurrency limits (_earlyRefreshConcurrencySemaphore)
    ///      to avoid overwhelming the system.
    /// 
    /// 5. Metrics & telemetry:
    ///    - Tracks per-key and global early refresh counts.
    ///    - Hooks available for cache hit/miss/refresh telemetry.
    /// 
    /// 6. Loader retries with exponential backoff.
    /// 7. Cancellation support for async operations.
    /// 8. Automatic cleanup of stale keys to prevent memory leaks.
    /// 9. Per-layer TTLs: Each cache layer can have its own TTL instead of sharing a single TTL across layers.
    /// </summary>
    public class MultilayerCacheManager<TKey, TValue> : IMultilayerCacheManager<TKey, TValue>
        where TKey : notnull
    {
        // Array of cache layers (e.g., memory, Redis)
        private readonly ICache<TKey, TValue>[] _layers;

        // Optional per-layer TTLs
        private readonly TimeSpan[] _layerTtls;

        // Loader function to fetch data on cache miss
        private readonly Func<TKey, CancellationToken, Task<TValue>> _loaderFunction;

        // Logger
        private readonly ILogger _logger;

        // Pluggable write policy (WriteThrough, WriteBehind)
        private readonly IWritePolicy<TKey, TValue> _writePolicy;

        // Persistent store writer delegate
        private readonly Func<TKey, TValue, Task> _persistentStoreWriter;

        // Tracks in-flight loader tasks for request coalescing
        private readonly ConcurrentDictionary<TKey, Lazy<Task<TValue>>> _inflight = new();

        // Optional per-key semaphore for ultra-hot keys
        private readonly ConcurrentDictionary<TKey, SemaphoreSlim> _keyLocks = new();

        // Tracks last refresh timestamp per key
        private readonly ConcurrentDictionary<TKey, DateTime> _lastRefresh = new();

        // Metrics for early refresh
        private readonly ConcurrentDictionary<TKey, int> _earlyRefreshCounts = new();
        private int _globalEarlyRefreshCount = 0;

        // TTL jitter to prevent cache stampedes
        private readonly double _ttlJitterFraction;
        private readonly Random _random = new();

        // Early refresh configuration
        private readonly TimeSpan _earlyRefreshThreshold;         // How close to TTL we trigger early refresh
        private readonly TimeSpan _minRefreshInterval;            // Minimum interval between refreshes per key
        private readonly SemaphoreSlim _earlyRefreshConcurrencySemaphore; // Limits global concurrent early refresh tasks

        // Promotion policy
        private readonly PromotionPolicy _promotionPolicy;

        // Automatic cleanup of stale keys
        private readonly TimeSpan _staleKeyCleanupInterval = TimeSpan.FromMinutes(10);
        private readonly Timer _cleanupTimer;

        // Optional telemetry hooks
        public Action<TKey>? OnCacheHit { get; set; }
        public Action<TKey>? OnCacheMiss { get; set; }
        public Action<TKey>? OnEarlyRefresh { get; set; }

        // Tracks number of hits/misses/early refresh per key
        private readonly ConcurrentDictionary<TKey, int> _accessCounts = new();

        

        /// <summary>
        /// Constructor
        /// </summary>
        public MultilayerCacheManager(
            ICache<TKey, TValue>[] layers,
            Func<TKey, CancellationToken, Task<TValue>> loaderFunction,
            ILogger logger,
            IWritePolicy<TKey, TValue>? writePolicy = null,
            TimeSpan? defaultTtl = null,
            TimeSpan[]? layerTtls = null,
            Func<TKey, TValue, Task>? persistentStoreWriter = null,
            TimeSpan? earlyRefreshThreshold = null,
            TimeSpan? minRefreshInterval = null,
            int maxConcurrentEarlyRefreshes = 10,
            double ttlJitterFraction = 0.1,
            PromotionPolicy promotionPolicy = PromotionPolicy.AllLayers)
        {
            _layers = layers ?? throw new ArgumentNullException(nameof(layers));
            _loaderFunction = loaderFunction ?? throw new ArgumentNullException(nameof(loaderFunction));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var ttl = defaultTtl ?? TimeSpan.FromMinutes(5);
            _writePolicy = writePolicy ?? new WriteThroughPolicy<TKey, TValue>(ttl);

            _layerTtls = new TimeSpan[layers.Length];
            if (layerTtls != null)
            {
                if (layerTtls.Length != layers.Length)
                    throw new ArgumentException("layerTtls length must match layers length");
                Array.Copy(layerTtls, _layerTtls, layers.Length);
            }
            else
            {
                for (int i = 0; i < layers.Length; i++)
                    _layerTtls[i] = ttl;
            }

            _persistentStoreWriter = persistentStoreWriter ?? ((key, value) =>
            {
                _logger.LogWarning("⚠ Persistent store writer not provided. Key {Key} written to caches but NOT persisted.", key);
                return Task.CompletedTask;
            });

            _earlyRefreshThreshold = earlyRefreshThreshold ?? TimeSpan.FromMinutes(1);
            _minRefreshInterval = minRefreshInterval ?? TimeSpan.FromSeconds(30);
            _earlyRefreshConcurrencySemaphore = new SemaphoreSlim(maxConcurrentEarlyRefreshes, maxConcurrentEarlyRefreshes);
            _ttlJitterFraction = Math.Clamp(ttlJitterFraction, 0, 1);
            _promotionPolicy = promotionPolicy;

            // Track cache hits/misses/early refresh for hot keys
            OnCacheHit = key => _accessCounts.AddOrUpdate(key, 1, (_, count) => count + 1);
            OnCacheMiss = key => _accessCounts.AddOrUpdate(key, 1, (_, count) => count + 1);
            OnEarlyRefresh = key => _accessCounts.AddOrUpdate(key, 1, (_, count) => count + 1);

            // Start periodic cleanup of stale keys
            _cleanupTimer = new Timer(CleanupStaleKeys, null, _staleKeyCleanupInterval, _staleKeyCleanupInterval);
        }

        /// <summary>
        /// Fetches a value from cache or loader. Implements request coalescing,
        /// early refresh, retries, and telemetry hooks.
        /// </summary>
        public async Task<TValue> GetOrAddAsync(TKey key, CancellationToken cancellationToken = default)
        {
            // Try each cache layer in order
            for (int i = 0; i < _layers.Length; i++)
            {
                try
                {
                    var (found, value) = await _layers[i].TryGetAsync(key);
                    if (found)
                    {
                        _logger.LogDebug("Cache hit at layer {Layer} for key {Key}", i, key);
                        OnCacheHit?.Invoke(key);

                        // Track access for top key statistics
                        _accessCounts.AddOrUpdate(key, 1, (_, c) => c + 1);

                        PromoteToHigherLayers(key, value, i);
                        TriggerEarlyRefresh(key);
                        return value;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error accessing layer {Layer} for key {Key}", i, key);
                }
            }

            // Cache miss
            OnCacheMiss?.Invoke(key);

            // Request coalescing using Lazy<Task>
            var lazyTask = _inflight.GetOrAdd(key, k => new Lazy<Task<TValue>>(async () =>
            {
                var sem = _keyLocks.GetOrAdd(k, _ => new SemaphoreSlim(1, 1));
                await sem.WaitAsync(cancellationToken);
                try
                {
                    // Retry logic with exponential backoff
                    int attempts = 0;
                    const int maxRetries = 3;
                    TimeSpan delay = TimeSpan.FromMilliseconds(100);

                    while (true)
                    {
                        try
                        {
                            var value = await _loaderFunction(k, cancellationToken);

                            // Apply TTL per layer and write via write policy
                            await _writePolicy.WriteAsync(k, value, _layers, _logger, _persistentStoreWriter, _layerTtls);

                            // Record last refresh accurately
                            _lastRefresh[k] = DateTime.UtcNow;

                            return value;
                        }
                        catch (Exception ex) when (attempts < maxRetries)
                        {
                            attempts++;
                            _logger.LogWarning(ex, "Loader failed for key {Key}, retry {Attempt}", k, attempts);
                            await Task.Delay(delay, cancellationToken);
                            delay *= 2;
                        }
                    }
                }
                finally
                {
                    sem.Release();
                    _inflight.TryRemove(k, out _);
                }
            }, LazyThreadSafetyMode.ExecutionAndPublication));

            return await lazyTask.Value;
        }

        /// <summary>
        /// Synchronous wrapper around GetOrAddAsync
        /// </summary>
        public TValue GetOrAdd(TKey key) => GetOrAddAsync(key).GetAwaiter().GetResult();

        /// <summary>
        /// Directly sets a value and updates last refresh timestamp
        /// </summary>
        public Task SetAsync(TKey key, TValue value)
        {
            _lastRefresh[key] = DateTime.UtcNow;
            return _writePolicy.WriteAsync(key, value, _layers, _logger, _persistentStoreWriter, _layerTtls);
        }

        /// <summary>
        /// Promotes a cached value to higher cache layers according to the configured promotion policy.
        /// </summary>
        private void PromoteToHigherLayers(TKey key, TValue value, int hitLayer)
        {
            if (_promotionPolicy == PromotionPolicy.None) return;

            int endLayer = _promotionPolicy == PromotionPolicy.FirstLayerOnly ? 0 : hitLayer;
            for (int j = 0; j < endLayer; j++)
            {
                try
                {
                    _layers[j].SetAsync(key, value, _layerTtls[j]).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to promote key {Key} to layer {Layer}", key, j);
                }
            }
        }

        /// <summary>
        /// Triggers background early refresh of cached items approaching TTL.
        /// Uses per-key throttling, global concurrency limits, jitter, and telemetry hooks.
        /// </summary>
        private void TriggerEarlyRefresh(TKey key)
        {
            if (!_lastRefresh.TryGetValue(key, out var lastRefresh)) return;

            var timeSinceLastRefresh = DateTime.UtcNow - lastRefresh;
            if (timeSinceLastRefresh < _writePolicy.DefaultTtl - _earlyRefreshThreshold) return;
            if (timeSinceLastRefresh < _minRefreshInterval) return;

            _ = SafeFireAndForget(async () =>
            {
                if (!await _earlyRefreshConcurrencySemaphore.WaitAsync(0)) return;

                try
                {
                    await Task.Delay(_random.Next(0, 500)); // small jitter delay
                    var refreshedValue = await _loaderFunction(key, CancellationToken.None);
                    await _writePolicy.WriteAsync(key, refreshedValue, _layers, _logger, WriteToLayers);

                    _lastRefresh[key] = DateTime.UtcNow;
                    _earlyRefreshCounts.AddOrUpdate(key, 1, (_, c) => c + 1);
                    Interlocked.Increment(ref _globalEarlyRefreshCount);

                    OnEarlyRefresh?.Invoke(key);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Early refresh failed for key {Key}", key);
                }
                finally
                {
                    _earlyRefreshConcurrencySemaphore.Release();
                }
            });
        }

        /// <summary>
        /// Executes a fire-and-forget task safely with exception logging.
        /// </summary>
        private async Task WriteToLayers(TKey key, TValue value)
        {
            for (int i = 0; i < _layers.Length; i++)
            {
                try
                {
                    await _layers[i].SetAsync(key, value, _layerTtls[i]);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to write key {Key} to layer {Layer}", key, i);
                }
            }
        }

        private async Task SafeFireAndForget(Func<Task> func)
        {
            try { await func(); }
            catch (Exception ex) { _logger.LogError(ex, "Unhandled exception in background task"); }
        }

        /// <summary>
        /// Adds random jitter (±_ttlJitterFraction) to the TTL to prevent cache stampede.
        /// </summary>
        private TimeSpan ApplyTtlJitter(TimeSpan baseTtl)
        {
            if (_ttlJitterFraction <= 0) return baseTtl;
            var jitter = (_random.NextDouble() * 2 - 1) * _ttlJitterFraction;
            return TimeSpan.FromTicks((long)(baseTtl.Ticks * (1 + jitter)));
        }

        /// <summary>
        /// Periodically cleans up stale keys from internal dictionaries to prevent memory leaks.
        /// </summary>
        private void CleanupStaleKeys(object? state)
        {
            var now = DateTime.UtcNow;
            var staleThreshold = TimeSpan.FromHours(1); // keys not refreshed for 1 hour

            foreach (var kvp in _lastRefresh)
            {
                if (now - kvp.Value > staleThreshold)
                {
                    _lastRefresh.TryRemove(kvp.Key, out _);
                    _inflight.TryRemove(kvp.Key, out _);
                    _keyLocks.TryRemove(kvp.Key, out _);
                    _earlyRefreshCounts.TryRemove(kvp.Key, out _);
                    _accessCounts.TryRemove(kvp.Key, out _);
                }
            }
        }

        /// <summary>
        /// Returns the number of early refreshes for a specific key
        /// </summary>
        public int GetEarlyRefreshCount(TKey key) =>
            _earlyRefreshCounts.TryGetValue(key, out var count) ? count : 0;

        /// <summary>
        /// Returns the global number of early refreshes
        /// </summary>
        public int GetGlobalEarlyRefreshCount() => _globalEarlyRefreshCount;

        /// <summary>
        /// Returns the top N most frequently accessed keys (hits + misses + early refreshes)
        /// </summary>
        public (TKey Key, int Count)[] GetTopKeys(int n)
        {
            return _accessCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(n)
                .Select(kvp => (kvp.Key, kvp.Value))
                .ToArray();
        }

        /// <summary>
        /// Returns a snapshot of the current metrics for this cache instance.
        /// </summary>
        /// <param name="topN">Number of top keys to return by access count</param>
        public CacheMetricsSnapshot<TKey> GetMetricsSnapshot(int topN = 10)
        {
            // Build the snapshot
            var snapshot = new CacheMetricsSnapshot<TKey>
            {
                // Access counts per key
                HitsPerKey = new Dictionary<TKey, int>(_accessCounts),

                // Early refresh counts per key
                EarlyRefreshCountPerKey = new Dictionary<TKey, int>(_earlyRefreshCounts),

                // Last refresh timestamps per key
                LastRefreshTimestamp = new Dictionary<TKey, DateTime>(_lastRefresh),

                // Keys currently being loaded
                InFlightKeys = new HashSet<TKey>(_inflight.Keys)
            };

            // Compute total hits
            snapshot.TotalHits = snapshot.HitsPerKey.Values.Sum();

            // Compute total early refreshes
            snapshot.TotalEarlyRefreshes = snapshot.EarlyRefreshCountPerKey.Values.Sum();

            // Compute top N keys by access count
            snapshot.TopKeysByAccessCount = snapshot.HitsPerKey
                .OrderByDescending(kv => kv.Value)
                .Take(topN)
                .Select(kv => kv.Key)
                .ToArray();

            // Latency is not tracked here; decorator can populate it

            return snapshot;
        }
    }
}
