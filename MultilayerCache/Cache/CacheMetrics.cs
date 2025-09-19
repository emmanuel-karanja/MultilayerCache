using System.Threading;

namespace MultilayerCache.Cache
{
    public class CacheMetrics
    {
        private long _hits;
        private long _misses;

        public long Hits => _hits;
        public long Misses => _misses;

        public void IncrementHit() => Interlocked.Increment(ref _hits);
        public void IncrementMiss() => Interlocked.Increment(ref _misses);
    }
}
