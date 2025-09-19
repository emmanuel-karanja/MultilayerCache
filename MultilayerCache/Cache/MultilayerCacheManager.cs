using System;
using System.Threading.Tasks;

namespace MultilayerCache.Cache
{
    public class MultilayerCacheManager<TKey, TValue>
    {
        private readonly ICache<TKey, TValue>[] _layers;

        public MultilayerCacheManager(params ICache<TKey, TValue>[] layers)
        {
            _layers = layers;
        }

        public void Set(TKey key, TValue value, TimeSpan ttl)
        {
            foreach (var layer in _layers)
                layer.Set(key, value, ttl);
        }

        public bool TryGet(TKey key, out TValue value)
        {
            for (int i = 0; i < _layers.Length; i++)
            {
                if (_layers[i].TryGet(key, out value))
                {
                    // Refresh upper layers
                    for (int j = 0; j < i; j++)
                        _layers[j].Set(key, value, TimeSpan.FromMinutes(5));
                    return true;
                }
            }
            value = default;
            return false;
        }

        public async Task SetAsync(TKey key, TValue value, TimeSpan ttl)
        {
            foreach (var layer in _layers)
                await layer.SetAsync(key, value, ttl);
        }

        public async Task<(bool found, TValue value)> TryGetAsync(TKey key)
        {
            for (int i = 0; i < _layers.Length; i++)
            {
                var (found, value) = await _layers[i].TryGetAsync(key);
                if (found)
                {
                    for (int j = 0; j < i; j++)
                        await _layers[j].SetAsync(key, value, TimeSpan.FromMinutes(5));
                    return (true, value);
                }
            }
            return (false, default);
        }
    }
}
