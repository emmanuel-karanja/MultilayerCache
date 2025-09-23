using Prometheus.Client.Collectors;
using MultilayerCache.Cache;
using MultilayerCache.Metrics;
using MultilayerCache.Demo;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        // --- Setup simple console logger ---
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("CacheDemo");

        // --- L1: In-memory cache ---
        var l1Cache = new InMemoryCache<string, User>(
            TimeSpan.FromMinutes(5),
            logger);

        // --- L2: Redis cache ---
        var options = ConfigurationOptions.Parse("localhost:6379");
        options.AllowAdmin = true;
        var redisCache = new RedisCache<string, User>(
            options.ToString(),
            logger);

        // --- Prometheus metrics registry ---
        var registry = new CollectorRegistry();

        // --- Metrics collector for both caches ---
        var collector = new CacheMetricsCollector<User>(
            l1Cache,
            redisCache,
            loggerFactory.CreateLogger<CacheMetricsCollector<User>>(),
            registry,
            TimeSpan.FromSeconds(5) // Collect every 5s
        );

        // --- Loader function for cache misses ---
        Task<User> LoaderFunction(string key)
        {
            logger.LogWarning("Loader invoked for missing key {Key}", key);
            // Simulate a DB fetch or external call
            return Task.FromResult(new User
            {
                Id = -1,
                Name = "LoadedFromSource",
                Email = "loaded@example.com"
            });
        }

        // --- Multilayer cache manager with request coalescing ---
        var cache = new MultilayerCacheManager<string, User>(
            new ICache<string, User>[] { l1Cache, redisCache },
            LoaderFunction,
            logger,
            defaultTtl: TimeSpan.FromMinutes(10)
        );

        // --- Populate the cache with users ---
        Console.WriteLine("Populating cache with 10,000 users...");
        for (int i = 0; i < 10_000; i++)
        {
            var user = new User
            {
                Id = i,
                Name = $"User {i}",
                Email = $"user{i}@example.com"
            };
            await cache.SetAsync($"user:{i}", user);
        }

        Console.WriteLine("Cache populated. Performing random lookups...");

        // --- Perform random lookups to generate cache hits/misses ---
        var rand = new Random();
        for (int i = 0; i < 5000; i++)
        {
            int id = rand.Next(0, 10_000);
            var result = await cache.GetOrAddAsync($"user:{id}");
            if (i % 1000 == 0)
            {
                Console.WriteLine($"Lookup {i}: Retrieved {result.Name}");
            }
        }

        // --- Let metrics collection run briefly ---
        Console.WriteLine("Metrics collection running for 30s...");
        await Task.Delay(TimeSpan.FromSeconds(30));

        collector.Dispose();

        Console.WriteLine("---- Final metrics ----");
        Console.WriteLine($"Memory Cache Hits: {l1Cache.Metrics.Hits}, Misses: {l1Cache.Metrics.Misses}");
        Console.WriteLine($"Redis Cache Hits: {redisCache.Metrics.Hits}, Misses: {redisCache.Metrics.Misses}");
    }
}
