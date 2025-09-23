using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MultilayerCache.Cache
{
    /// <summary>
    /// Manages multi-layer caching. Supports pluggable write policies,
    /// loader functions, persistent store writes, and request coalescing.
    /// </summary>
    public class MultilayerCacheManager<TKey, TValue>
    {
        private readonly ICache<TKey, TValue>[] _layers;
        private readonly Func<TKey, Task<TValue>> _loaderFunction;
        private readonly ILogger _logger;
        private readonly IWritePolicy<TKey, TValue> _writePolicy;
        private readonly Func<TKey, TValue, Task> _persistentStoreWriter;

        // Tracks in-flight loader tasks for request coalescing
        private readonly ConcurrentDictionary<TKey, Lazy<Task<TValue>>> _inflight =
            new ConcurrentDictionary<TKey, Lazy<Task<TValue>>>();

        public MultilayerCacheManager(
            ICache<TKey, TValue>[] layers,
            Func<TKey, Task<TValue>> loaderFunction,
            ILogger logger,
            IWritePolicy<TKey, TValue>? writePolicy = null,
            TimeSpan? defaultTtl = null,
            Func<TKey, TValue, Task>? persistentStoreWriter = null)
        {
            _layers = layers ?? throw new ArgumentNullException(nameof(layers));
            _loaderFunction = loaderFunction ?? throw new ArgumentNullException(nameof(loaderFunction));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            var ttl = defaultTtl ?? TimeSpan.FromMinutes(5);
            _writePolicy = writePolicy ?? new WriteThroughPolicy<TKey, TValue>(ttl);

            // Default writer logs a warning if no persistent store is provided
            _persistentStoreWriter = persistentStoreWriter ?? ((key, value) =>
            {
                _logger.LogWarning(
                    "⚠ Persistent store writer not provided. Key {Key} written to caches but NOT persisted.",
                    key);
                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// Attempts to get a value from cache layers or loads it via the loader function.
        /// Coalesces concurrent requests for the same key.
        /// </summary>
        public async Task<TValue> GetOrAddAsync(TKey key)
        {
            // 1️. Try all cache layers first
            for (int i = 0; i < _layers.Length; i++)
            {
                try
                {
                    var (found, value) = await _layers[i].TryGetAsync(key);
                    if (found)
                    {
                        _logger.LogDebug("Cache hit at layer {Layer} for key {Key}", i, key);

                        // Promote to upper layers if needed
                        for (int j = 0; j < i; j++)
                        {
                            try
                            {
                                await _layers[j].SetAsync(key, value, TimeSpan.FromMinutes(5));
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to promote key {Key} to layer {Layer}", key, j);
                            }
                        }

                        return value;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error accessing layer {Layer} for key {Key}", i, key);
                }
            }

            // 2️. Request coalescing: ensure only one loader call per key
            var lazyTask = _inflight.GetOrAdd(key, k =>
                new Lazy<Task<TValue>>(async () =>
                {
                    try
                    {
                        var loaded = await _loaderFunction(k);
                        _logger.LogDebug("Cache miss for key {Key}, loaded via loader", k);

                        // Write through to caches and persistent store
                        await _writePolicy.WriteAsync(k, loaded, _layers, _logger, _persistentStoreWriter);

                        return loaded;
                    }
                    finally
                    {
                        // Remove from inflight once finished
                        _inflight.TryRemove(k, out _);
                    }
                }));

            // Await the shared task
            return await lazyTask.Value;
        }

        /// <summary>Synchronous wrapper around GetOrAddAsync.</summary>
        public TValue GetOrAdd(TKey key) => GetOrAddAsync(key).GetAwaiter().GetResult();

        /// <summary>Directly sets a value using the write policy.</summary>
        public Task SetAsync(TKey key, TValue value) =>
            _writePolicy.WriteAsync(key, value, _layers, _logger, _persistentStoreWriter);
    }
}
