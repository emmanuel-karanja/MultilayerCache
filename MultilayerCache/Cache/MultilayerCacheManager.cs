using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MultilayerCache.Cache
{
    /// <summary>
    /// Manages multi-layer caching. Supports pluggable write policies and a loader function.
    /// Now supports an optional persistent store writer delegate.
    /// </summary>
    public class MultilayerCacheManager<TKey, TValue>
    {
        private readonly ICache<TKey, TValue>[] _layers;
        private readonly Func<TKey, Task<TValue>> _loaderFunction;
        private readonly ILogger _logger;
        private readonly IWritePolicy<TKey, TValue> _writePolicy;
        private readonly Func<TKey, TValue, Task>? _persistentStoreWriter;

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

             // Default writer: logs a warning but does nothing
            _persistentStoreWriter = persistentStoreWriter ?? ((key, value) =>
            {
                _logger.LogWarning(
                    "⚠ Persistent store writer not provided. Key {Key} was written to caches but NOT persisted.",
                    key);
                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// Get a value from the cache or load it if not present.
        /// </summary>
        public async Task<TValue> GetOrAddAsync(TKey key)
        {
            // 1. Check all cache layers
            for (int i = 0; i < _layers.Length; i++)
            {
                try
                {
                    var (found, value) = await _layers[i].TryGetAsync(key);
                    if (found)
                    {
                        _logger.LogDebug("Cache hit at layer {Layer} for key {Key}", i, key);

                        // Promote to higher layers if found deeper
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

            // 2. Cache miss → use loader function
            TValue loadedValue = await _loaderFunction(key);
            _logger.LogDebug("Cache miss for key {Key}, loaded via loader function", key);

            // 3. Write to cache layers and persistent store
           
            await _writePolicy.WriteAsync(key, loadedValue, _layers, _logger, _persistentStoreWriter);
            
            return loadedValue;
        }

        /// <summary>
        /// Synchronous wrapper around GetOrAddAsync.
        /// </summary>
        public TValue GetOrAdd(TKey key) => GetOrAddAsync(key).GetAwaiter().GetResult();

        /// <summary>
        /// Directly set a value using the write policy.
        /// </summary>
        public Task SetAsync(TKey key, TValue value)
        {
            return _writePolicy.WriteAsync(key, value, _layers, _logger, _persistentStoreWriter);
        }
    }
}
