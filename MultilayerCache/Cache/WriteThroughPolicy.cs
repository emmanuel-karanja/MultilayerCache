using Microsoft.Extensions.Logging;

namespace MultilayerCache.Cache
{
    /// <summary>
    /// Implements a write-through caching policy.
    /// Writes data to all cache layers and a persistent store on every write.
    /// Supports optional per-layer TTLs.
    /// </summary>
    public class WriteThroughPolicy<TKey, TValue> : IWritePolicy<TKey, TValue>
        where TKey : notnull
    {
        /// <summary>
        /// Default TTL for cached items.
        /// </summary>
        public TimeSpan DefaultTtl { get; }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="ttl">Time-to-live for cached items.</param>
        public WriteThroughPolicy(TimeSpan ttl)
        {
            DefaultTtl = ttl;
        }

        /// <summary>
        /// Writes the value to all cache layers and the persistent store.
        /// Supports per-layer TTLs.
        /// </summary>
        /// <param name="key">Cache key.</param>
        /// <param name="value">Value to cache and persist.</param>
        /// <param name="layers">Cache layers (e.g., in-memory, Redis).</param>
        /// <param name="logger">Logger for diagnostic messages.</param>
        /// <param name="persistentStoreWriter">Delegate to write to the persistent store.</param>
        /// <param name="ttls">Optional per-layer TTLs. Must match layers length if provided.</param>
        public async Task WriteAsync(
            TKey key,
            TValue value,
            ICache<TKey, TValue>[] layers,
            ILogger logger,
            Func<TKey, TValue, Task> persistentStoreWriter,
            TimeSpan[]? ttls = null)
        {
            if (ttls != null && ttls.Length != layers.Length)
                throw new ArgumentException("ttls length must match layers length");

            // 1️. Write to all cache layers
            for (int i = 0; i < layers.Length; i++)
            {
                try
                {
                    var ttl = ttls != null ? ttls[i] : DefaultTtl;
                    await layers[i].SetAsync(key, value, ttl);
                    logger.LogDebug("Write-through wrote key {Key} to layer {Layer}", key, layers[i].GetType().Name);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Write-through failed for key {Key} in layer {Layer}", key, layers[i].GetType().Name);
                }
            }

            // 2. Write to persistent store
            if (persistentStoreWriter != null)
            {
                try
                {
                    await persistentStoreWriter(key, value);
                    logger.LogInformation("Write-through persisted key {Key}", key);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Write-through persistent store write failed for key {Key}", key);
                    throw; // Fail fast to signal persistence failure
                }
            }
            else
            {
                logger.LogWarning("No persistent store writer provided for key {Key}", key);
            }
        }
    }
}
