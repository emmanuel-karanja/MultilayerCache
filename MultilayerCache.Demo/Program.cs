using MultilayerCache.Cache;
using MultilayerCache.Demo;
using MultilayerCache.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Context;

// --- Determine project root and ensure appsettings.json is loaded safely ---
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

// --- Configure Serilog ---
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .Enrich.WithProperty("ServiceName", "MultilayerCache.Demo")
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: config["Logging:OutputTemplate"]
                        ?? "[{Timestamp:HH:mm:ss} {Level:u3}] {ServiceName} {ClassName} {Message:lj}{NewLine}{Exception}"
    )
    .CreateLogger();

try
{
    Log.Information("Starting MultilayerCache demo...");

    // --- Configure DI ---
    var services = new ServiceCollection();
    services.Configure<CacheOptions>(config.GetSection("Cache"));
    services.Configure<MultilayerCacheOptions>(config.GetSection("MultilayerCache"));
    services.Configure<RedisResilienceOptions>(config.GetSection("RedisResilience"));
    services.AddLogging(builder => builder.AddSerilog());

    using var provider = services.BuildServiceProvider();

    var cacheOptions = provider.GetRequiredService<IOptions<CacheOptions>>().Value;
    var mlCacheOptions = provider.GetRequiredService<IOptions<MultilayerCacheOptions>>().Value;
    var redisResilienceOptions = provider.GetRequiredService<IOptions<RedisResilienceOptions>>().Value;

    var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
    var cacheLogger = loggerFactory.CreateLogger("CacheDemo");

    // --- Setup L1 InMemoryCache ---
    var memoryCache = new InMemoryCache<string, User>(
        TimeSpan.FromSeconds(cacheOptions.MemoryCacheCleanupIntervalSeconds),
        cacheLogger);

    // --- Setup L2 RedisCache with resilience ---
    var redisConnection = cacheOptions.Redis.ConnectionString;
    cacheLogger.LogInformation("Connecting to Redis at {RedisConnection}", redisConnection);

    // Declare and initialize in one step to avoid nullable warnings
    var redisCache = new RedisCache<string, User>(
        redisConnection,
        cacheLogger,
        redisResilienceOptions);

    // Loader function for cache misses
    async Task<User> LoaderFunction(string key)
    {
        using (LogContext.PushProperty("ClassName", "LoaderFunction"))
        {
            cacheLogger.LogWarning("Loader invoked for missing key {Key}", key);
        }
        return await Task.FromResult(new User
        {
            Id = -1,
            Name = "LoadedFromSource",
            Email = "loaded@example.com"
        });
    }

    // --- Multilayer cache manager with L1 + L2 ---
   var cache = new MultilayerCacheManager<string, User>(
        new ICache<string, User>[] { memoryCache, redisCache }, // ✅ explicit type
        LoaderFunction,
        cacheLogger,
        defaultTtl: mlCacheOptions.DefaultTtl,
        earlyRefreshThreshold: mlCacheOptions.EarlyRefreshThreshold,
        minRefreshInterval: mlCacheOptions.MinRefreshInterval,
        maxConcurrentEarlyRefreshes: mlCacheOptions.MaxConcurrentEarlyRefreshes
    );


    // --- Demo run ---
    var random = new Random();
    int redisFallbacks = 0;

    using (LogContext.PushProperty("ClassName", "Program"))
    {
        Log.Information("Caching {TotalItems:N0} users...", cacheOptions.TotalItems);

        for (int i = 1; i <= cacheOptions.TotalItems; i++)
        {
            var user = new User { Id = i, Name = $"User {i}", Email = $"user{i}@example.com" };
            await cache.SetAsync($"user:{i}", user);

            if (i % 1000 == 0)
                Log.Information("{Count:N0} users cached...", i);
        }

        Log.Information("✅ All users cached.");
        Log.Information("Accessing 500 random users immediately (L1 hits expected)...");

        for (int j = 0; j < 500; j++)
        {
            int id = random.Next(1, cacheOptions.TotalItems + 1);
            await cache.GetOrAddAsync($"user:{id}");
        }

        Log.Information("Waiting {Wait}s for memory cache to expire...", cacheOptions.WaitForExpirySeconds);
        await Task.Delay(TimeSpan.FromSeconds(cacheOptions.WaitForExpirySeconds));

        Log.Information("Accessing 2000 random users after memory expiration (L2 hits expected)...");
        for (int j = 0; j < 2000; j++)
        {
            int id = random.Next(1, cacheOptions.TotalItems + 1);
            var user = await cache.GetOrAddAsync($"user:{id}");
            if (user != null) redisFallbacks++;
        }

        Log.Information("Accessing 500 random *non-existent* users...");
        for (int j = 0; j < 500; j++)
        {
            int fakeId = cacheOptions.TotalItems + random.Next(1, 1000);
            await cache.GetOrAddAsync($"user:{fakeId}");
        }

        Console.WriteLine();
        Console.WriteLine("---- Metrics ----");
        Console.WriteLine($"Memory Cache (L1) Hits: {memoryCache.Metrics.Hits}, Misses: {memoryCache.Metrics.Misses}");
        Console.WriteLine($"Redis Cache (L2) Approx Hits (fallbacks after L1 miss): {redisFallbacks}");
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "Unhandled exception in MultilayerCache demo");
}
finally
{
    Log.CloseAndFlush();
}
