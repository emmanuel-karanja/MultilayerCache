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

var builder = WebApplication.CreateBuilder(args);

// --- Load configuration ---
builder.Configuration
       .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
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

// --- Register loader ---
builder.Services.AddSingleton<Func<string, Task<User>>>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Program>>();
    return async key =>
    {
        using (LogContext.PushProperty("ClassName", "LoaderFunction"))
        {
            logger.LogWarning("Loader invoked for missing key {Key}", key);
        }
        await Task.Delay(5); // simulate DB/network latency
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
    var loader = sp.GetRequiredService<Func<string, Task<User>>>();
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

    // --- Telemetry hooks ---
    cacheManager.OnCacheHit = key =>
    {
        logger.LogInformation("Cache HIT for key {Key}", key);
        // TODO: increment custom metrics or OpenTelemetry counters
    };

    cacheManager.OnCacheMiss = key =>
    {
        logger.LogWarning("Cache MISS for key {Key}", key);
        // TODO: increment custom metrics or OpenTelemetry counters
    };

    cacheManager.OnEarlyRefresh = key =>
    {
        logger.LogInformation("Early refresh triggered for key {Key}", key);
        // TODO: increment custom metrics or OpenTelemetry counters
    };

    return cacheManager;
});

// --- Instrumentation decorator ---
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
        Description = "API demonstrating MultilayerCache with OpenTelemetry and Serilog."
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
        c.RoutePrefix = string.Empty; // optional: Swagger at root
    });
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
