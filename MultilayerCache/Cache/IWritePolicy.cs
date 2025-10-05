using Microsoft.Extensions.Logging;

namespace MultilayerCache.Cache
{
    public interface IWritePolicy<TKey, TValue>
        where TKey : notnull
    {
        TimeSpan DefaultTtl { get; }
        
        /// <summary>
        /// Writes a value to cache layers and a persistent store.
        /// Supports optional per-layer TTLs.
        /// </summary>
        Task WriteAsync(
            TKey key,
            TValue value,
            ICache<TKey, TValue>[] layers,
            ILogger logger,
            Func<TKey, TValue, Task> persistentStoreWriter,
            TimeSpan[]? ttls = null
        );
    }
}
