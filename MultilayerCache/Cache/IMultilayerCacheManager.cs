namespace MultilayerCache.Cache
{
    using System.Threading.Tasks;

/// <summary>
/// Interface for MultilayerCacheManager to enable decoration and instrumentation.
/// </summary>
public interface IMultilayerCacheManager<TKey, TValue>
    where TKey : notnull
{
    /// <summary>
    /// Fetches a value from cache or loader asynchronously
    /// </summary>
    Task<TValue> GetOrAddAsync(TKey key);

    /// <summary>
    /// Fetches a value from cache or loader synchronously
    /// </summary>
    TValue GetOrAdd(TKey key);

    /// <summary>
    /// Sets a value in the cache asynchronously
    /// </summary>
    Task SetAsync(TKey key, TValue value);

    /// <summary>
    /// Returns the number of early refreshes for a specific key
    /// </summary>
    int GetEarlyRefreshCount(TKey key);

    /// <summary>
    /// Returns the global number of early refreshes
    /// </summary>
    int GetGlobalEarlyRefreshCount();
}

}