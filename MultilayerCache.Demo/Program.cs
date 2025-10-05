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
using Microsoft.AspNetCore.Http;
using NBomber.CSharp;
using NBomber.Contracts;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

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
    return new InMemoryCache<string, User>(TimeSpan.FromSeconds(opts.MemoryCacheCleanupIntervalSeconds), logger);
});

builder.Services.AddSingleton<RedisCache<string, User>>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<CacheOptions>>().Value;
    var redisOpts = sp.GetRequiredService<IOptions<RedisResilienceOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<RedisCache<string, User>>>();
    return new RedisCache<string, User>(opts.Redis.ConnectionString, logger, redisOpts);
});

// --- Register loader ---
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
        return new User { Id = -1, Name = "LoadedFromSource", Email = "loaded@example.com" };
    };
});

// --- Multilayer cache manager ---
builder.Services.AddSingleton<MultilayerCacheManager<string, User>>(sp =>
{
    var memCache = sp.GetRequiredService<InMemoryCache<string, User>>();
    var redisCache = sp.GetRequiredService<RedisCache<string, User>>();
    var loader = sp.GetRequiredService<Func<string, CancellationToken, Task<User>>>();
    var logger = sp.GetRequiredService<ILogger<MultilayerCacheManager<string, User>>>();
    var opts = sp.GetRequiredService<IOptions<MultilayerCacheOptions>>().Value;

    var cacheManager = new MultilayerCacheManager<string, User>(
        new ICache<string, User>[] { memCache, redisCache },
        loader,
        logger,
        defaultTtl: opts.DefaultTtl,
        earlyRefreshThreshold: opts.EarlyRefreshThreshold,
        minRefreshInterval: opts.MinRefreshInterval,
        maxConcurrentEarlyRefreshes: opts.MaxConcurrentEarlyRefreshes
    );

    cacheManager.OnCacheHit = key => Console.WriteLine($"[Telemetry] HIT: {key}");
    cacheManager.OnCacheMiss = key => Console.WriteLine($"[Telemetry] MISS: {key}");
    cacheManager.OnEarlyRefresh = key => Console.WriteLine($"[Telemetry] EARLY REFRESH: {key}");

    return cacheManager;
});

// --- Optional instrumentation decorator ---
builder.Services.AddSingleton<InstrumentedCacheManagerDecorator<string, User>>(sp =>
{
    var baseCache = sp.GetRequiredService<MultilayerCacheManager<string, User>>();
    return new InstrumentedCacheManagerDecorator<string, User>(baseCache);
});

// --- Register controllers & Swagger ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

// --- Seed cache keys ---
var cacheManager = app.Services.GetRequiredService<MultilayerCacheManager<string, User>>();
await cacheManager.SetAsync("user:1", new User { Id = 1, Name = "Preloaded1", Email = "user1@example.com" });
await cacheManager.SetAsync("user:2", new User { Id = 2, Name = "Preloaded2", Email = "user2@example.com" });

// --- Manual cache endpoints ---
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

// --- Optional metrics endpoint ---
app.MapGet("/api/cache/metrics", () =>
{
    var snapshot = cacheManager.GetMetricsSnapshot(topN: 20);
    return Results.Json(snapshot);
});

// --- Periodic metrics dump to JSON file ---
var metricsFilePath = "cache_metrics.json";
var metricsDumpInterval = TimeSpan.FromSeconds(30);

_ = Task.Run(async () =>
{
    while (true)
    {
        try
        {
            var snapshot = cacheManager.GetMetricsSnapshot(topN: 20);
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            });
            await File.WriteAllTextAsync(metricsFilePath, json);
            Console.WriteLine($"[Metrics] Dumped to {metricsFilePath} at {DateTime.Now}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Metrics] Failed to write metrics: {ex}");
        }

        await Task.Delay(metricsDumpInterval);
    }
});

// --- Run NBomber in background ---
var random = new Random();
_ = Task.Run(() =>
{
    var scenario = Scenario.Create("multilayer_cache_load_test", async context =>
    {
        var key = $"user:{random.Next(1, 5000)}";
        var value = await cacheManager.GetOrAddAsync(key);
        return Response.Ok(value);
    })
    .WithLoadSimulations(Simulation.KeepConstant(50, TimeSpan.FromMinutes(1)));

    var stats = NBomberRunner.RegisterScenarios(scenario).Run();
    Console.WriteLine($"NBomber test finished. Total OK requests: {stats.AllOkCount}");
});

// --- Graceful shutdown ---
Console.CancelKeyPress += (s, e) =>
{
    Console.WriteLine("Stopping web server...");
    // NBomber finishes, web server stops
};

// --- Run web server ---
await app.RunAsync();
