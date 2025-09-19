using System;
using System.Threading.Tasks;

namespace MultilayerCache.Cache
{
    public interface ICache<TKey, TValue>
    {
        void Set(TKey key, TValue value, TimeSpan ttl);
        bool TryGet(TKey key, out TValue value);

        Task SetAsync(TKey key, TValue value, TimeSpan ttl);
        Task<(bool found, TValue value)> TryGetAsync(TKey key);
    }
}
