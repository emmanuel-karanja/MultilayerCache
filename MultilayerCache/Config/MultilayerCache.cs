namespace MultilayerCache.Config
{
    public class CacheOptions
    {
        public int MemoryCacheCleanupIntervalSeconds { get; set; }
        public int DefaultTtlMinutes { get; set; }
        public RedisOptions Redis { get; set; } = new();
        public int TotalItems { get; set; }
        public int WaitForExpirySeconds { get; set; }
    }

    public class RedisOptions
    {
        public string ConnectionString { get; set; } = string.Empty;
    }
}
