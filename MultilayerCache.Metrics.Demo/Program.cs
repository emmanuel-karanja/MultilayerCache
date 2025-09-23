using Prometheus.Client.Collectors;
using MultilayerCache.Cache;
using MultilayerCache.Metrics;
using MultilayerCache.Demo;
using MultilayerCache.Config;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        // --- Setup configuration ---
        var projectRoot = Path.Combine(AppContext.BaseDirectory, "..", "..", "..");
        var configPath = Path.Combine(projectRoot, "appsettings.json");
        if (!File.Exists(configPath))
        {
            Console.WriteLine($"ERROR: Configuration file not found at {configPath}");
            return;
        }

        var config = new ConfigurationBuilder()
            .SetBasePath(projectRoot)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        // --- Setup console logger ---
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger("CacheDemo");

        // --- Load Redis resilience settings ---
        var redisResilienceOptions = config.GetSection("RedisResilience").Get<RedisResilienceOptions>() 
                                     ?? new RedisResilienceOptions();

        // --- L1: In-memory cache ---
        var l1Cache = new InMemoryCache<string, User>(
            TimeSpan.FromMinutes(5),
            logger);

        // --- L2: Redis cache with resilience ---
        var redisConnection = config.GetSection("Cache:Redis:ConnectionString").Value ?? "localhost:6379";
        var redisCache = new RedisCache<string, User>(
            redisConnection,
            logger,
            redisResilienceOptions);

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

        // --- Loader function ---
        Task<User> LoaderFunction(string key)
        {
            logger.LogWarning("Loader invoked for missing key {Key}", key);
            return Task.FromResult(new User
            {
                Id = -1,
                Name = "LoadedFromSource",
                Email = "loaded@example.com"
            });
        }

        // --- Multilayer cache manager ---
        var cache = new MultilayerCacheManager<string, User>(
            new ICache<string, User>[] { l1Cache, redisCache }, // explicit interface array
            LoaderFunction,
            logger,
            defaultTtl: TimeSpan.FromMinutes(10)
        );

        // --- Populate cache with 10,000 users ---
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

        // --- Random lookups ---
        var rand = new Random();
        for (int i = 0; i < 5000; i++)
        {
            int id = rand.Next(0, 10_000);
            var result = await cache.GetOrAddAsync($"user:{id}");
            if (i % 1000 == 0)
            {
                Console.WriteLine($"Lookup {i}: Retrieved {result?.Name}");
            }
        }

        // --- Let metrics run briefly ---
        Console.WriteLine("Metrics collection running for 30s...");
        await Task.Delay(TimeSpan.FromSeconds(30));

        collector.Dispose();

        // --- Final metrics ---
        Console.WriteLine("---- Final metrics ----");
        Console.WriteLine($"Memory Cache Hits: {l1Cache.Metrics.Hits}, Misses: {l1Cache.Metrics.Misses}");
        Console.WriteLine($"Redis Cache Hits: {redisCache.Metrics.Hits}, Misses: {redisCache.Metrics.Misses}");
    }
}
