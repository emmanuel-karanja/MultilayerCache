using System;

namespace MultilayerCache.Cache
{
    public class CacheItem<TValue>
    {
        public TValue Value { get; }
        public DateTime ExpiryTime { get; }

        public CacheItem(TValue value, TimeSpan ttl)
        {
            Value = value;
            ExpiryTime = DateTime.UtcNow.Add(ttl);
        }

        public bool IsExpired => DateTime.UtcNow >= ExpiryTime;
    }
}
