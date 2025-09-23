using MultilayerCache.Cache;
using MultilayerCache.Demo;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;

class Program
{
    static async Task Main()
    {
        // --- Configure Serilog ---
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        // Create Microsoft logger factory wired to Serilog
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSerilog();
        });

        var cacheLogger = loggerFactory.CreateLogger("CacheDemo");

        var random = new Random();

        // --- Setup caches with logging ---
        var memoryCache = new InMemoryCache<string, User>(
            TimeSpan.FromSeconds(30),
            cacheLogger);

        var redisCache = new RedisCache<string, User>(
            "localhost,allowAdmin=true",
            cacheLogger);

        // Loader function: in a real app, this might query a DB or API.
        // For demo, we simulate a miss by creating a fake user.
        Task<User> LoaderFunction(string key)
        {
            cacheLogger.LogWarning("Loader invoked for missing key {Key}", key);
            return Task.FromResult(new User
            {
                Id = -1,
                Name = "LoadedFromSource",
                Email = "loaded@example.com"
            });
        }

        // Multilayer cache manager with WriteThrough as default policy
        var cache = new MultilayerCacheManager<string, User>(
            new ICache<string, User>[] { memoryCache, redisCache },
            LoaderFunction,
            cacheLogger);

        const int totalItems = 10_000;
        int redisFallbacks = 0;

        Log.Information("Caching {TotalItems:N0} users...", totalItems);

        // --- Insert users ---
        for (int i = 1; i <= totalItems; i++)
        {
            var user = new User
            {
                Id = i,
                Name = $"User {i}",
                Email = $"user{i}@example.com"
            };

            await cache.SetAsync($"user:{i}", user);

            if (i % 1000 == 0)
                Log.Information("{Count:N0} users cached...", i);
        }

        Log.Information("✅ All users cached.");

        // --- Immediate access: L1 hits expected ---
        Log.Information("Accessing 2000 random users immediately (L1 hits expected)...");
        for (int j = 0; j < 2000; j++)
        {
            int id = random.Next(1, totalItems + 1);
            await cache.GetOrAddAsync($"user:{id}");
        }

        // --- Let L1 expire ---
        Log.Information("Waiting 40s for memory cache to expire...");
        await Task.Delay(TimeSpan.FromSeconds(40));

        // --- Access again: L2 hits expected ---
        Log.Information("Accessing 2000 random users after memory expiration (L2 hits expected)...");
        for (int j = 0; j < 2000; j++)
        {
            int id = random.Next(1, totalItems + 1);
            var user = await cache.GetOrAddAsync($"user:{id}");
            if (user != null) redisFallbacks++;
        }

        // --- Access non-existent keys ---
        Log.Information("Accessing 500 random *non-existent* users...");
        for (int j = 0; j < 500; j++)
        {
            int fakeId = totalItems + random.Next(1, 1000);
            await cache.GetOrAddAsync($"user:{fakeId}");
        }

        // --- Metrics ---
        Console.WriteLine();
        Console.WriteLine("---- Metrics ----");
        Console.WriteLine($"Memory Cache (L1) Hits: {memoryCache.Metrics.Hits}, Misses: {memoryCache.Metrics.Misses}");
        Console.WriteLine($"Redis Cache   (L2) Approx Hits (fallbacks after L1 miss): {redisFallbacks}");

        Log.CloseAndFlush();
    }
}
