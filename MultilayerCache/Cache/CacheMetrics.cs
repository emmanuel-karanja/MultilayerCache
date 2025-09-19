using System.Threading;

namespace MultilayerCache.Cache
{
    public class CacheMetrics
    {
        private long _hits;
        private long _misses;

        /// <summary>
        /// Total cache hits
        /// </summary>
        public long Hits => Interlocked.Read(ref _hits);

        /// <summary>
        /// Total cache misses
        /// </summary>
        public long Misses => Interlocked.Read(ref _misses);

        /// <summary>
        /// Total items (hits + misses) for reporting
        /// </summary>
        public long ItemCount => Hits + Misses;

        /// <summary>
        /// Increment hits
        /// </summary>
        public void IncrementHit() => Interlocked.Increment(ref _hits);

        /// <summary>
        /// Increment misses
        /// </summary>
        public void IncrementMiss() => Interlocked.Increment(ref _misses);
    }
}
