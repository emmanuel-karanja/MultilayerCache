using System;
using System.Collections.Generic;
using System.Linq;

namespace MultilayerCache.Cache
{
    /// <summary>
    /// Snapshot of all relevant metrics from a single MultilayerCacheManager instance.
    /// </summary>
    public class CacheMetricsSnapshot<TKey>
        where TKey : notnull
    {
        /// <summary>Number of hits per key</summary>
        public IReadOnlyDictionary<TKey, int> HitsPerKey { get; init; } = new Dictionary<TKey, int>();

        /// <summary>Number of early refreshes per key</summary>
        public IReadOnlyDictionary<TKey, int> EarlyRefreshesPerKey { get; init; } = new Dictionary<TKey, int>();

        /// <summary>Timestamp of last refresh per key</summary>
        public IReadOnlyDictionary<TKey, DateTime> LastRefreshPerKey { get; init; } = new Dictionary<TKey, DateTime>();

        /// <summary>Keys currently being loaded (in-flight)</summary>
        public IReadOnlyList<TKey> InflightKeys { get; init; } = new List<TKey>();

        /// <summary>Total global early refresh count</summary>
        public int GlobalEarlyRefreshCount { get; init; }

        /// <summary>Top N keys by access count</summary>
        public IReadOnlyList<TKey> TopKeysByAccessCount { get; init; } = new List<TKey>();
    }
}
