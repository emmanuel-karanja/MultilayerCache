using MultilayerCache.Cache;
using MultilayerCache.Demo;
using MultilayerCache.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;
using System;
using System.IO;
using System.Threading.Tasks;

// --- Load configuration ---
var config = new ConfigurationBuilder()
    .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "MultilayerCache.Demo"))
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables() // Allow overriding via environment variables
    .Build();

// --- Configure Serilog ---
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(outputTemplate: config["Logging:OutputTemplate"])
    .CreateLogger();

// --- Bind CacheOptions and set up DI ---
var services = new ServiceCollection();
services.Configure<CacheOptions>(config.GetSection("Cache"));
services.AddLogging(builder => builder.AddSerilog());

using var provider = services.BuildServiceProvider();
var options = provider.GetRequiredService<IOptions<CacheOptions>>().Value;
var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
var cacheLogger = loggerFactory.CreateLogger("CacheDemo");

// --- Setup caches ---
var memoryCache = new InMemoryCache<string, User>(
    TimeSpan.FromSeconds(options.MemoryCacheCleanupIntervalSeconds),
    cacheLogger);

// Build Redis connection string using externalized host and port
var redisConnection = options.Redis.ConnectionString;
cacheLogger.LogInformation("Connecting to Redis at {RedisConnection}", redisConnection);

var redisCache = new RedisCache<string, User>(
    redisConnection,
    cacheLogger);

// Loader function for cache misses
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

// Create cache manager
var cache = new MultilayerCacheManager<string, User>(
    new ICache<string, User>[] { memoryCache, redisCache },
    LoaderFunction,
    cacheLogger,
    defaultTtl: TimeSpan.FromMinutes(options.DefaultTtlMinutes));

// --- Demo run ---
var random = new Random();
int redisFallbacks = 0;

Log.Information("Caching {TotalItems:N0} users...", options.TotalItems);

for (int i = 1; i <= options.TotalItems; i++)
{
    var user = new User { Id = i, Name = $"User {i}", Email = $"user{i}@example.com" };
    await cache.SetAsync($"user:{i}", user);

    if (i % 1000 == 0)
        Log.Information("{Count:N0} users cached...", i);
}

Log.Information("✅ All users cached.");
Log.Information("Accessing 2000 random users immediately (L1 hits expected)...");

for (int j = 0; j < 500; j++)
{
    int id = random.Next(1, options.TotalItems + 1);
    await cache.GetOrAddAsync($"user:{id}");
}

Log.Information("Waiting {Wait}s for memory cache to expire...", options.WaitForExpirySeconds);
await Task.Delay(TimeSpan.FromSeconds(options.WaitForExpirySeconds));

Log.Information("Accessing 2000 random users after memory expiration (L2 hits expected)...");
for (int j = 0; j < 2000; j++)
{
    int id = random.Next(1, options.TotalItems + 1);
    var user = await cache.GetOrAddAsync($"user:{id}");
    if (user != null) redisFallbacks++;
}

Log.Information("Accessing 500 random *non-existent* users...");
for (int j = 0; j < 500; j++)
{
    int fakeId = options.TotalItems + random.Next(1, 1000);
    await cache.GetOrAddAsync($"user:{fakeId}");
}

Console.WriteLine();
Console.WriteLine("---- Metrics ----");
Console.WriteLine($"Memory Cache (L1) Hits: {memoryCache.Metrics.Hits}, Misses: {memoryCache.Metrics.Misses}");
Console.WriteLine($"Redis Cache   (L2) Approx Hits (fallbacks after L1 miss): {redisFallbacks}");

Log.CloseAndFlush();
