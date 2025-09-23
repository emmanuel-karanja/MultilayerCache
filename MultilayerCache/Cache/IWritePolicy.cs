
namespace MultilayerCache.Cache;
public interface IWritePolicy<TKey, TValue>
{
    Task WriteAsync(TKey key, TValue value, ICache<TKey, TValue>[] layers, ILogger logger);
}
