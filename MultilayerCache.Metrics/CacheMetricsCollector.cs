using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using MultilayerCache.Cache;
using Google.Protobuf;
using Prometheus.Client;
using Prometheus.Client.Collectors;

namespace MultilayerCache.Metrics
{
    public class CacheMetricsCollector<TValue> : IDisposable
        where TValue : IMessage<TValue>
    {
        private readonly InMemoryCache<string, TValue> _l1Cache;
        private readonly RedisCache<string, TValue> _l2Cache;
        private readonly ILogger<CacheMetricsCollector<TValue>> _logger;
        private readonly CancellationTokenSource _cts = new();

        private readonly IGauge _l1Items;
        private readonly IGauge _l2UsedMemory;
        private readonly IGauge _l2HitRatio;

        private readonly TimeSpan _collectionInterval;

        public CacheMetricsCollector(
            InMemoryCache<string, TValue> l1Cache,
            RedisCache<string, TValue> l2Cache,
            ILogger<CacheMetricsCollector<TValue>> logger,
            ICollectorRegistry registry,
            TimeSpan? collectionInterval = null)
        {
            _l1Cache = l1Cache ?? throw new ArgumentNullException(nameof(l1Cache));
            _l2Cache = l2Cache ?? throw new ArgumentNullException(nameof(l2Cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _collectionInterval = collectionInterval ?? TimeSpan.FromSeconds(5);

            // Create MetricFactory using registry
            var factory = new MetricFactory(registry);

            _l1Items = factory.CreateGauge("l1_memorycache_items_total", "Total items in L1 MemoryCache");
            _l2UsedMemory = factory.CreateGauge("l2_redis_used_memory_bytes", "Redis used memory in bytes");
            _l2HitRatio = factory.CreateGauge("l2_redis_hit_ratio_percent", "Redis cache hit ratio percentage");

            // Start background metrics collection
            Task.Run(() => CollectMetricsLoopAsync(_cts.Token));
        }

        public void AddL1Sample(string key, TValue value, TimeSpan ttl)
        {
            _l1Cache.Set(key, value, ttl);
        }

        private async Task CollectMetricsLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    CollectL1Metrics();
                    await CollectL2MetricsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error collecting cache metrics.");
                }

                await Task.Delay(_collectionInterval, token);
            }
        }

        private void CollectL1Metrics()
        {
            try
            {
                // Example: total hits + misses as "item count"
                _l1Items.Set(_l1Cache.Metrics.Hits + _l1Cache.Metrics.Misses);
                _logger.LogInformation("Logging");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to collect L1 metrics.");
            }
        }

        private async Task CollectL2MetricsAsync()
        {
            try
            {
                long totalUsedMemory = 0;

                var multiplexer = _l2Cache.Multiplexer;
                foreach (var endpoint in multiplexer.GetEndPoints())
                {
                    var server = multiplexer.GetServer(endpoint);
                    var info = await server.InfoAsync("memory", CommandFlags.None);

                    foreach (var section in info)
                    {
                        foreach (var pair in section)
                        {
                            if (pair.Key == "used_memory")
                                totalUsedMemory += long.Parse(pair.Value);
                        }
                    }
                }

                _l2UsedMemory.Set(totalUsedMemory);

                double hitRatio = (_l1Cache.Metrics.Hits + _l1Cache.Metrics.Misses) == 0
                    ? 0
                    : (double)_l1Cache.Metrics.Hits / (_l1Cache.Metrics.Hits + _l1Cache.Metrics.Misses) * 100;

                _l2HitRatio.Set(hitRatio);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to collect L2 metrics.");
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
        }
    }
}
