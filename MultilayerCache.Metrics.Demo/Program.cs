using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Prometheus.Client;
using Prometheus.Client.Collectors;
using MultilayerCache.Cache;
using MultilayerCache.Metrics;
using MultilayerCache.Demo;
using StackExchange.Redis;

class Program
{
    static async Task Main()
    {
        // Setup in-memory cache (L1)
        var l1Cache = new InMemoryCache<string, User>(TimeSpan.FromMinutes(5));

        // Setup Redis cache (L2) with AllowAdmin = true
        var options = ConfigurationOptions.Parse("localhost:6379");
        options.AllowAdmin = true;
        var redisCache = new RedisCache<string, User>(options.ToString());

        // Setup a simple logger
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<CacheMetricsCollector<User>>();

        // Create Prometheus registry
        var registry = new CollectorRegistry();

        // Create metrics collector
        var collector = new CacheMetricsCollector<User>(
            l1Cache,
            redisCache,
            logger,
            registry,
            TimeSpan.FromSeconds(5) // collect every 5s
        );

        // Setup multilayer cache
        var cache = new MultilayerCacheManager<string, User>(l1Cache, redisCache);

        // Insert 10,000 random users
        Console.WriteLine("Populating cache with 10,000 users...");
        for (int i = 0; i < 10000; i++)
        {
            var user = new User
            {
                Id = i,
                Name = $"User {i}",
                Email = $"user{i}@example.com"
            };
            await cache.SetAsync($"user:{i}", user, TimeSpan.FromMinutes(10));
        }

        Console.WriteLine("Cache populated. Performing random lookups...");

        var rand = new Random();
        for (int i = 0; i < 5000; i++)
        {
            int id = rand.Next(0, 10000);
            var (found, user) = await cache.TryGetAsync($"user:{id}");
            if (found && i % 1000 == 0)
            {
                Console.WriteLine($"Cache hit: {user.Name}");
            }
        }

        // Let metrics collection run for a while
        Console.WriteLine("Metrics collection running for 30s...");
        await Task.Delay(TimeSpan.FromSeconds(30));

        collector.Dispose();

        Console.WriteLine("Final metrics:");
        Console.WriteLine($"Memory Cache Hits: {l1Cache.Metrics.Hits}, Misses: {l1Cache.Metrics.Misses}");
    }
}
