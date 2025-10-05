using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MultilayerCache.Cache;
using MultilayerCache.Config;
using Serilog;
using Serilog.Context;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Microsoft.Extensions.Options;
using MultilayerCache.Demo;
using System.Linq;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

// --- Load configuration ---
builder.Configuration
       .AddJsonFile("MultilayerCache.Demo\\appsettings.json", optional: false, reloadOnChange: true)
       .AddEnvironmentVariables();

// --- Configure Serilog ---
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();

// --- Configure OpenTelemetry ---
builder.Services.AddOpenTelemetry()
    .WithTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource("MultilayerCache.Demo")
            .SetSampler(new AlwaysOnSampler())
            .AddConsoleExporter();
    })
    .WithMetrics(metricsProviderBuilder =>
    {
        metricsProviderBuilder
            .AddMeter("MultilayerCache.Instrumentation")
            .AddRuntimeInstrumentation()
            .AddHttpClientInstrumentation()
            .AddConsoleExporter();
    });

// --- Configure options ---
builder.Services.Configure<CacheOptions>(builder.Configuration.GetSection("Cache"));
builder.Services.Configure<MultilayerCacheOptions>(builder.Configuration.GetSection("MultilayerCache"));
builder.Services.Configure<RedisResilienceOptions>(builder.Configuration.GetSection("RedisResilience"));

// --- Register caches ---
builder.Services.AddSingleton<InMemoryCache<string, User>>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<CacheOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<InMemoryCache<string, User>>>();
    return new InMemoryCache<string, User>(
        TimeSpan.FromSeconds(opts.MemoryCacheCleanupIntervalSeconds),
        logger
    );
});

builder.Services.AddSingleton<RedisCache<string, User>>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<CacheOptions>>().Value;
    var redisOpts = sp.GetRequiredService<IOptions<RedisResilienceOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<RedisCache<string, User>>>();
    return new RedisCache<string, User>(opts.Redis.ConnectionString, logger, redisOpts);
});

// --- Register loader with configurable test behavior ---
builder.Services.AddSingleton<Func<string, CancellationToken, Task<User>>>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Program>>();
    return async (key, token) =>
    {
        using (LogContext.PushProperty("ClassName", "LoaderFunction"))
        {
            logger.LogWarning("Loader invoked for missing key {Key}", key);
        }
        await Task.Delay(5, token); // simulate DB/network latency
        return new User
        {
            Id = -1,
            Name = "LoadedFromSource",
            Email = "loaded@example.com"
        };
    };
});


// --- Multilayer cache manager with telemetry hooks ---
builder.Services.AddSingleton<MultilayerCacheManager<string, User>>(sp =>
{
    var memCache = sp.GetRequiredService<InMemoryCache<string, User>>();
    var redisCache = sp.GetRequiredService<RedisCache<string, User>>();
    var loader = sp.GetRequiredService<Func<string, CancellationToken, Task<User>>>(); // fixed
    var logger = sp.GetRequiredService<ILogger<MultilayerCacheManager<string, User>>>();
    var opts = sp.GetRequiredService<IOptions<MultilayerCacheOptions>>().Value;

    var cacheManager = new MultilayerCacheManager<string, User>(
        new ICache<string, User>[] { memCache, redisCache },
        loader, // now matches Func<string, CancellationToken, Task<User>>
        logger,
        defaultTtl: opts.DefaultTtl,
        earlyRefreshThreshold: opts.EarlyRefreshThreshold,
        minRefreshInterval: opts.MinRefreshInterval,
        maxConcurrentEarlyRefreshes: opts.MaxConcurrentEarlyRefreshes
    );

    // --- Telemetry hooks ---
    cacheManager.OnCacheHit = key => Console.WriteLine($"[Telemetry] HIT: {key}");
    cacheManager.OnCacheMiss = key => Console.WriteLine($"[Telemetry] MISS: {key}");
    cacheManager.OnEarlyRefresh = key => Console.WriteLine($"[Telemetry] EARLY REFRESH: {key}");

    return cacheManager;
});


// --- Instrumentation decorator (optional) ---
builder.Services.AddSingleton<InstrumentedCacheManagerDecorator<string, User>>(sp =>
{
    var baseCache = sp.GetRequiredService<MultilayerCacheManager<string, User>>();
    return new InstrumentedCacheManagerDecorator<string, User>(baseCache);
});

// --- Controllers & Swagger ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "MultilayerCache API",
        Version = "v1",
        Description = "API demonstrating MultilayerCache with all test scenarios."
    });
});

var app = builder.Build();

// --- Middleware ---
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "MultilayerCache API v1");
        c.RoutePrefix = string.Empty;
    });
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// --- Seed some keys for testing ---
var cacheManager = app.Services.GetRequiredService<MultilayerCacheManager<string, User>>();

await cacheManager.SetAsync("user:1", new User { Id = 1, Name = "Preloaded1", Email = "user1@example.com" });
await cacheManager.SetAsync("user:2", new User { Id = 2, Name = "Preloaded2", Email = "user2@example.com" });

// --- Background task to trigger early refresh manually ---
_ = Task.Run(async () =>
{
    while (!app.Lifetime.ApplicationStopping.IsCancellationRequested)
    {
        await Task.Delay(5000); // check every 5s
        await cacheManager.GetOrAddAsync("user:1"); // triggers soft TTL / early refresh if threshold reached
    }
});

// --- Controller for manual testing ---
app.MapGet("/api/cache/{key}", async (string key) =>
{
    var value = await cacheManager.GetOrAddAsync(key);
    return Results.Ok(new
    {
        Key = key,
        Value = value,
        EarlyRefreshCount = cacheManager.GetEarlyRefreshCount(key)
    });
});

app.MapGet("/api/cache/parallel/{key}/{count:int}", async (string key, int count) =>
{
    var tasks = Enumerable.Range(0, count)
        .Select(_ => cacheManager.GetOrAddAsync(key));
    var results = await Task.WhenAll(tasks);

    return Results.Ok(new
    {
        Key = key,
        Values = results.Select(v => v.Name).ToArray(),
        EarlyRefreshCount = cacheManager.GetEarlyRefreshCount(key)
    });
});

app.Run();
