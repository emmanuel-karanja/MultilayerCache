using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MultilayerCache.Cache
{
    // Multilayer cache manager
    public class MultilayerCacheManager<TKey, TValue>
    {
        private readonly ICache<TKey, TValue>[] _layers;
        private readonly Func<TKey, Task<TValue>> _loaderFunction;
        private readonly ILogger _logger;
        private readonly IWritePolicy<TKey, TValue> _writePolicy;

        public MultilayerCacheManager(
            ICache<TKey, TValue>[] layers,
            Func<TKey, Task<TValue>> loaderFunction,
            ILogger logger,
            IWritePolicy<TKey, TValue>? writePolicy = null,
            TimeSpan? defaultTtl = null)
        {
            _layers = layers ?? throw new ArgumentNullException(nameof(layers));
            _loaderFunction = loaderFunction ?? throw new ArgumentNullException(nameof(loaderFunction));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Default TTL for write-through policy
            var ttl = defaultTtl ?? TimeSpan.FromMinutes(5);
            _writePolicy = writePolicy ?? new WriteThroughPolicy<TKey, TValue>(ttl);
        }

        // Get or add a value
        public async Task<TValue> GetOrAddAsync(TKey key)
        {
            // 1. Check all layers
            for (int i = 0; i < _layers.Length; i++)
            {
                try
                {
                    var (found, value) = await _layers[i].TryGetAsync(key);
                    if (found)
                    {
                        _logger.LogDebug("Cache hit at layer {Layer} for key {Key}", i, key);

                        // Promote to upper layers
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

            // 2. Load via loader function
            TValue loadedValue = await _loaderFunction(key);
            _logger.LogDebug("Cache miss for key {Key}, loaded via loader function", key);

            // 3. Write to all layers via policy
            await _writePolicy.WriteAsync(key, loadedValue, _layers, _logger);

            return loadedValue;
        }

        // Optional synchronous wrapper
        public TValue GetOrAdd(TKey key)
        {
            return GetOrAddAsync(key).GetAwaiter().GetResult();
        }

        // Direct set using write policy
        public Task SetAsync(TKey key, TValue value)
        {
            return _writePolicy.WriteAsync(key, value, _layers, _logger);
        }
    }
}
