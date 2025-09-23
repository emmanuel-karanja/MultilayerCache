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
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 6379;
        public string ConnectionString => $"{Host}:{Port}";
    }
}
