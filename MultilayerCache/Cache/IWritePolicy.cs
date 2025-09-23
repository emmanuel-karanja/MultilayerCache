
using Microsoft.Extensions.Logging;

namespace MultilayerCache.Cache;
public interface IWritePolicy<TKey, TValue>
 where TKey: notnull
{
     Task WriteAsync(
        TKey key, 
        TValue value, 
        ICache<TKey, TValue>[] layers, 
        ILogger logger,
        Func<TKey, TValue, Task> persistentStoreWriter // <- Added delegate
    );
}
