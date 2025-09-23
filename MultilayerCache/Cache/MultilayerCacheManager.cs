using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MultilayerCache.Cache
{
    /// <summary>
    /// Manages multi-layer caching with the following advanced features:
    /// 
    /// 1. Multi-layer caching: Supports an array of cache layers (e.g., in-memory, Redis) with automatic promotion
    ///    of frequently accessed items to higher (faster) layers.
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
    ///    to avoid overwhelming the system.
    /// 
    /// 5. Metrics:
    ///    - Tracks per-key and global early refresh counts for monitoring cache health and refresh behavior.
    /// 
    /// Usage:
    /// - GetOrAddAsync: Fetches a value from cache or loader, coalesces requests, triggers early refresh if needed.
    /// - GetOrAdd: Synchronous wrapper around GetOrAddAsync.
    /// - SetAsync: Directly sets a value and updates refresh timestamp.
    /// - TriggerEarlyRefresh: Internal method to handle background soft TTL refresh safely.
    /// 
    /// Designed for read-heavy systems where caching and early refresh reduce latency and persistent store load.
    /// </summary>
    public class MultilayerCacheManager<TKey, TValue>
        where TKey : notnull
    {
        // Array of cache layers (e.g., memory, Redis)
        private readonly ICache<TKey, TValue>[] _layers;

        // Loader function to fetch data on cache miss
        private readonly Func<TKey, Task<TValue>> _loaderFunction;

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
        private readonly double _ttlJitterFraction; // e.g., 0.1 = ±10% jitter
        private readonly Random _random = new();


        // Configuration for early refresh
        private readonly TimeSpan _earlyRefreshThreshold;         // How close to TTL we trigger early refresh
        private readonly TimeSpan _minRefreshInterval;            // Minimum interval between refreshes per key
        private readonly SemaphoreSlim _earlyRefreshConcurrencySemaphore; // Limits global concurrent early refresh tasks

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="layers">Cache layers</param>
        /// <param name="loaderFunction">Function to fetch value on cache miss</param>
        /// <param name="logger">Logger instance</param>
        /// <param name="writePolicy">Optional write policy</param>
        /// <param name="defaultTtl">Default TTL for cached items</param>
        /// <param name="persistentStoreWriter">Optional persistent store writer</param>
        /// <param name="earlyRefreshThreshold">Time before TTL to trigger early refresh</param>
        /// <param name="minRefreshInterval">Minimum interval between early refresh per key</param>
        /// <param name="maxConcurrentEarlyRefreshes">Max number of concurrent early refresh tasks</param>
        /// <param name="ttlJitterFraction">jitter for TTL</param>
        public MultilayerCacheManager(
            ICache<TKey, TValue>[] layers,
            Func<TKey, Task<TValue>> loaderFunction,
            ILogger logger,
            IWritePolicy<TKey, TValue>? writePolicy = null,
            TimeSpan? defaultTtl = null,
            Func<TKey, TValue, Task>? persistentStoreWriter = null,
            TimeSpan? earlyRefreshThreshold = null,
            TimeSpan? minRefreshInterval = null,
            int maxConcurrentEarlyRefreshes = 10,
            double ttlJitterFraction=0.1)
        {
            _layers = layers ?? throw new ArgumentNullException(nameof(layers));
            _loaderFunction = loaderFunction ?? throw new ArgumentNullException(nameof(loaderFunction));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var ttl = defaultTtl ?? TimeSpan.FromMinutes(5);
            _writePolicy = writePolicy ?? new WriteThroughPolicy<TKey, TValue>(ttl);

            _persistentStoreWriter = persistentStoreWriter ?? ((key, value) =>
            {
                _logger.LogWarning("⚠ Persistent store writer not provided. Key {Key} written to caches but NOT persisted.", key);
                return Task.CompletedTask;
            });

            _earlyRefreshThreshold = earlyRefreshThreshold ?? TimeSpan.FromMinutes(1);
            _minRefreshInterval = minRefreshInterval ?? TimeSpan.FromSeconds(30);
            _earlyRefreshConcurrencySemaphore = new SemaphoreSlim(maxConcurrentEarlyRefreshes, maxConcurrentEarlyRefreshes);
            _ttlJitterFraction = Math.Clamp(ttlJitterFraction, 0, 1); // ensure valid fraction
        }

        /// <summary>
        /// Fetches a value from cache or loader. Implements request coalescing and triggers early refresh if needed.
        /// </summary>
       public async Task<TValue> GetOrAddAsync(TKey key)
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

                        // Bubble up changes to higher cache layers
                        for (int j = 0; j < i; j++)
                        {
                            try
                            {
                                // add jitter to the TTL 
                                var ttlWithJitter = ApplyTtlJitter(_writePolicy.DefaultTtl);
                                await _layers[j].SetAsync(key, value, ttlWithJitter);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to promote key {Key} to layer {Layer}", key, j);
                            }
                        }

                        // TODO: (Look at how this could be made better if it's a problem. For now
                        // Fire and Forget trigger early refresh,
                        TriggerEarlyRefresh(key);
                        return value;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error accessing layer {Layer} for key {Key}", i, key);
                }
            }

            // Request coalescing using Lazy<Task>, Thundering Herd mitigation
            // Prevents multiple calls to the loader for the same key
            var lazyTask = _inflight.GetOrAdd(key, k =>
                new Lazy<Task<TValue>>(async () =>
                {
                    try
                    {
                        var sem = _keyLocks.GetOrAdd(k, _ => new SemaphoreSlim(1, 1));
                        await sem.WaitAsync();
                        try
                        {
                            var loadedValue = await _loaderFunction(k);
                            _logger.LogDebug("Cache miss for key {Key}, loaded via loader", k);

                            // Apply jitter to TTL when writing via write policy
                            var ttlWithJitter = ApplyTtlJitter(_writePolicy.DefaultTtl);
                            await _writePolicy.WriteAsync(k, loadedValue, _layers, _logger,
                                async (writeKey, writeValue) =>
                                {
                                    foreach (var layer in _layers)
                                        await layer.SetAsync(writeKey, writeValue, ttlWithJitter);
                                });

                            _lastRefresh[k] = DateTime.UtcNow;

                            return loadedValue;
                        }
                        finally
                        {
                            sem.Release();
                        }
                    }
                    finally
                    {
                        _inflight.TryRemove(k, out _);
                    }
                }, LazyThreadSafetyMode.ExecutionAndPublication) // Only one thread runs the initializer. 
                                                                // All other threads wait for the value. Once initialized, the same value is returned to all threads.
            );

            return await lazyTask.Value;
        }


        /// <summary>
        /// Synchronous wrapper around GetOrAddAsync
        /// </summary>
        public TValue GetOrAdd(TKey key) => GetOrAddAsync(key).GetAwaiter().GetResult();

        /// <summary>
        /// Sets a value and updates last refresh timestamp
        /// </summary>
       public Task SetAsync(TKey key, TValue value)
        {
            _lastRefresh[key] = DateTime.UtcNow;

            // Apply TTL jitter
            var ttlWithJitter = ApplyTtlJitter(_writePolicy.DefaultTtl);

            return _writePolicy.WriteAsync(key, value, _layers, _logger, _persistentStoreWriter, ttlWithJitter);
        }


        /// <summary>
        /// Triggers a background early refresh if the cached value is approaching TTL. This will mitigate the Cache Stampede with keys
        /// expiring right around the same time. We can add a jitter too. TODO: Add jitter to ttl
        /// Uses per-key throttling and global concurrency limiting.
        /// </summary>
        private void TriggerEarlyRefresh(TKey key)
        {
            if (_lastRefresh.TryGetValue(key, out var lastRefresh))
            {
                var timeSinceLastRefresh = DateTime.UtcNow - lastRefresh;

                // Skip if not close to TTL
                if (timeSinceLastRefresh < _writePolicy.DefaultTtl - _earlyRefreshThreshold)
                    return;

                // Skip if refreshed recently
                if (timeSinceLastRefresh < _minRefreshInterval)
                    return;

                //initiate the refresh background task
                // TODO: Batch such requests together and do a single fetch and jitter TTLs to prevent them
                //expiring at the same time. Configure, configure, configure it seems
                _ = Task.Run(async () =>
                {
                    if (!await _earlyRefreshConcurrencySemaphore.WaitAsync(0))
                    {
                        _logger.LogDebug("Skipping early refresh for key {Key} due to concurrency limit", key);
                        return;
                    }

                    try
                    {
                        var refreshedValue = await _loaderFunction(key);

                        // Apply jitter to TTL before writing
                        var ttlWithJitter = ApplyTtlJitter(_writePolicy.DefaultTtl);
                        await _writePolicy.WriteAsync(key, refreshedValue, _layers, _logger,
                            async (k, v) =>
                            {
                                // Write to all layers with jittered TTL
                                foreach (var layer in _layers)
                                    await layer.SetAsync(k, v, ttlWithJitter);
                            });

                        // Update last refresh timestamp
                        _lastRefresh[key] = DateTime.UtcNow;

                        var count = _earlyRefreshCounts.AddOrUpdate(key, 1, (_, c) => c + 1);
                        Interlocked.Increment(ref _globalEarlyRefreshCount);

                        _logger.LogInformation("Early refresh completed for key {Key}. Per-key: {Count}, Global: {GlobalCount}",
                            key, count, _globalEarlyRefreshCount);
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
        }

        /// <summary>
        /// Adds random jitter (±10%) to the TTL to prevent cache stampede.
        /// </summary>
        private TimeSpan ApplyTtlJitter(TimeSpan baseTtl)
        {
            if (_ttlJitterFraction <= 0) return baseTtl;

            // Random fraction between -_ttlJitterFraction and +_ttlJitterFraction
            var jitter = (_random.NextDouble() * 2 - 1) * _ttlJitterFraction;
            var jitteredTicks = (long)(baseTtl.Ticks * (1 + jitter));
            return TimeSpan.FromTicks(jitteredTicks);
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
        
    }
}
