using System;
using System.Collections.Generic;

namespace MultilayerCache.Cache
{
    /// <summary>
    /// Represents a snapshot of cache metrics for analysis, monitoring, or reporting.
    /// Captures raw data per cache key and global aggregates.
    /// </summary>
    /// <typeparam name="TKey">Type of the cache key.</typeparam>
    public class CacheMetricsSnapshot<TKey>
        where TKey : notnull
    {
        /// <summary>
        /// Number of cache hits per key.
        /// Useful for identifying the most frequently accessed keys.
        /// </summary>
        public Dictionary<TKey, int> HitsPerKey { get; set; } = new();

        /// <summary>
        /// Number of cache misses per key.
        /// Useful to detect hot keys that frequently miss or to tune TTL/loader behavior.
        /// </summary>
        public Dictionary<TKey, int> MissesPerKey { get; set; } = new();

        /// <summary>
        /// Last recorded latency per key in milliseconds.
        /// Includes time to fetch from cache or backend on miss.
        /// </summary>
        public Dictionary<TKey, double> LastLatencyPerKey { get; set; } = new();

        /// <summary>
        /// Number of early refreshes performed per key.
        /// Indicates keys that are approaching TTL frequently and may benefit from optimization.
        /// </summary>
        public Dictionary<TKey, int> EarlyRefreshCountPerKey { get; set; } = new();

        /// <summary>
        /// Timestamp of the last successful refresh for each key.
        /// Useful to calculate staleness or to identify outdated cache entries.
        /// </summary>
        public Dictionary<TKey, DateTime> LastRefreshTimestamp { get; set; } = new();

        /// <summary>
        /// Number of times each key has been promoted from lower cache layers to higher layers.
        /// Helps analyze caching efficiency and promotion behavior.
        /// </summary>
        public Dictionary<TKey, int> PromotionCountPerKey { get; set; } = new();

        /// <summary>
        /// Keys that are currently being loaded from the backend.
        /// Useful to monitor in-flight operations and detect potential thundering herd issues.
        /// </summary>
        public HashSet<TKey> InFlightKeys { get; set; } = new();

        /// <summary>
        /// Total number of cache hits across all keys.
        /// Provides a global hit rate indicator.
        /// </summary>
        public int TotalHits { get; set; }

        /// <summary>
        /// Total number of cache misses across all keys.
        /// Provides insight into backend load and cache effectiveness.
        /// </summary>
        public int TotalMisses { get; set; }

        /// <summary>
        /// Total number of promotions across all layers.
        /// Helps track how often keys move between layers.
        /// </summary>
        public int TotalPromotions { get; set; }

        /// <summary>
        /// Total number of early refreshes across all keys.
        /// Useful for evaluating background refresh frequency and load.
        /// </summary>
        public int TotalEarlyRefreshes { get; set; }

        /// <summary>
        /// The top N keys by access count.
        /// Helps identify hot keys that dominate cache usage.
        /// </summary>
        public TKey[] TopKeysByAccessCount { get; set; } = Array.Empty<TKey>();
    }
}
