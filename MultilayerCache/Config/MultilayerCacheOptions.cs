namespace MultilayerCache.Config
{
    /// <summary>
    /// Strongly-typed configuration for MultilayerCacheManager
    /// </summary>
    public class MultilayerCacheOptions
    {
        /// <summary>Default TTL for cached items</summary>
        public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>Time before TTL to trigger early refresh</summary>
        public TimeSpan EarlyRefreshThreshold { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>Minimum interval between early refreshes for a key</summary>
        public TimeSpan MinRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Maximum number of concurrent early refresh tasks globally</summary>
        public int MaxConcurrentEarlyRefreshes { get; set; } = 10;

        /// <summary>Optional: Number of items to prepopulate in cache at startup</summary>
        public int PreloadItemCount { get; set; } = 0;

        /// <summary>Optional: Metrics logging interval in seconds</summary>
        public int MetricsLoggingIntervalSeconds { get; set; } = 60;
    }
}
