using Microsoft.Extensions.Logging;

namespace MultilayerCache.Cache;
/// <summary>
    /// Implements a write-behind caching policy.
    /// Writes immediately to the fastest cache layer, then propagates asynchronously
    /// to other layers and a persistent store.
    /// </summary>
    public class WriteBehindPolicy<TKey, TValue> : IWritePolicy<TKey, TValue>
    {
        private readonly TimeSpan _ttl;

        public WriteBehindPolicy(TimeSpan ttl) => _ttl = ttl;

        /// <summary>
        /// Write-behind strategy: write synchronously to L1, then asynchronously to the other layers and persistent store.
        /// </summary>
        /// <param name="key">Cache key.</param>
        /// <param name="value">Value to cache and persist.</param>
        /// <param name="layers">Cache layers.</param>
        /// <param name="logger">Logger instance.</param>
        /// <param name="persistentStoreWriter">Delegate to write the value to the persistent store.</param>
        public async Task WriteAsync(
            TKey key,
            TValue value,
            ICache<TKey, TValue>[] layers,
            ILogger logger,
            Func<TKey, TValue, Task> persistentStoreWriter)
        {
            // 1. Write synchronously to the first (fastest) layer
            try
            {
                await layers[0].SetAsync(key, value, _ttl);
                logger.LogDebug("Write-behind wrote key {Key} to first layer {Layer}", key, layers[0].GetType().Name);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Write-behind failed for key {Key} in top layer", key);
            }

            // 2. Asynchronous propagation to other layers and persistent store
            _ = Task.Run(async () =>
            {
                for (int i = 1; i < layers.Length; i++)
                {
                    try
                    {
                        await layers[i].SetAsync(key, value, _ttl);
                        logger.LogDebug("Write-behind propagated key {Key} to layer {Layer}", key, layers[i].GetType().Name);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Write-behind failed for key {Key} in layer {Layer}", key, layers[i].GetType().Name);
                    }
                }

                if (persistentStoreWriter != null)
                {
                    try
                    {
                        await persistentStoreWriter(key, value);
                        logger.LogInformation("Write-behind persisted key {Key}", key);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Write-behind persistent store write failed for key {Key}", key);
                    }
                }
                else
                {
                    logger.LogWarning("No persistent store writer provided for key {Key}", key);
                }
            });
        }
    }