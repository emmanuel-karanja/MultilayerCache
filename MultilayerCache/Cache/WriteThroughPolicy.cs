using Microsoft.Extensions.Logging;

namespace MultilayerCache.Cache;
/// <summary>
    /// Implements a write-through caching policy.
    /// Writes data to all cache layers and a persistent store on every write.
    /// </summary>
    public class WriteThroughPolicy<TKey, TValue> : IWritePolicy<TKey, TValue>
    {
        private readonly TimeSpan _ttl;

        public WriteThroughPolicy(TimeSpan ttl) => _ttl = ttl;

        /// <summary>
        /// Writes the value to all cache layers and the persistent store.
        /// </summary>
        /// <param name="key">Cache key.</param>
        /// <param name="value">Value to cache and persist.</param>
        /// <param name="layers">Cache layers (e.g., in-memory, Redis).</param>
        /// <param name="logger">Logger for diagnostic messages.</param>
        /// <param name="persistentStoreWriter">Delegate to write to the persistent store.</param>
        public async Task WriteAsync(
            TKey key,
            TValue value,
            ICache<TKey, TValue>[] layers,
            ILogger logger,
            Func<TKey, TValue, Task> persistentStoreWriter)
        {
            // 1. Write to all cache layers
            foreach (var layer in layers)
            {
                try
                {
                    await layer.SetAsync(key, value, _ttl);
                    logger.LogDebug("Wrote key {Key} to {Layer}", key, layer.GetType().Name);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Write-through failed for key {Key} in layer {Layer}", key, layer.GetType().Name);
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
                    logger.LogError(ex, "Persistent store write failed for key {Key}", key);
                    throw; // Fail fast to signal persistence failure
                }
            }
            else
            {
                logger.LogWarning("No persistent store writer provided for key {Key}", key);
            }
        }
}
