using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Prometheus.Client;
using Prometheus.Client.Collectors;
using MultilayerCache.Cache;
using MultilayerCache.Metrics;

class Program
{
    static async Task Main()
    {
        // Setup in-memory cache
        var l1Cache = new InMemoryCache<string, SampleMessage>(TimeSpan.FromMinutes(5));

        // Setup Redis cache (replace with your Redis connection string)
        var l2Cache = new RedisCache<string, SampleMessage>("localhost:6379");

        // Setup a simple logger
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<CacheMetricsCollector<SampleMessage>>();

        // Create Prometheus registry
        var registry = new CollectorRegistry();

        // Create metrics collector
        var collector = new CacheMetricsCollector<SampleMessage>(
            l1Cache,
            l2Cache,
            logger,
            registry,
            TimeSpan.FromSeconds(5)
        );

        // Add some sample items
        collector.AddL1Sample("item1", new SampleMessage { Data = "Hello" }, TimeSpan.FromMinutes(1));
        collector.AddL1Sample("item2", new SampleMessage { Data = "World" }, TimeSpan.FromMinutes(1));

        // Run metrics collection loop for demo
        Console.WriteLine("Metrics collection running. Press Ctrl+C to exit...");
        await Task.Delay(TimeSpan.FromSeconds(20));

        collector.Dispose();
    }
}
