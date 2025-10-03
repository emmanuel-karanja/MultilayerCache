using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;

namespace MultilayerCache.Cache
{
    /// <summary>
    /// Decorator for IMultilayerCacheManager that adds instrumentation for metrics.
    /// Measures latency, throughput, and operation counts without modifying the original cache logic.
    /// </summary>
    public class InstrumentedCacheManagerDecorator<TKey, TValue> : IMultilayerCacheManager<TKey, TValue>
        where TKey : notnull
    {
        private readonly IMultilayerCacheManager<TKey, TValue> _inner;

        // Meter for OpenTelemetry metrics
        private static readonly Meter _meter = new("MultilayerCache.Instrumentation", "1.0.0");

        private readonly Counter<long> _operationCounter;
        private readonly Histogram<double> _latencyHistogram;

        // Track latency per key for quick inspection
        private readonly ConcurrentDictionary<TKey, double> _latencyPerKey = new();

        public InstrumentedCacheManagerDecorator(IMultilayerCacheManager<TKey, TValue> inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));

            _operationCounter = _meter.CreateCounter<long>(
                "cache_operations_total",
                description: "Total number of cache operations");

            _latencyHistogram = _meter.CreateHistogram<double>(
                "cache_operation_latency_ms",
                unit: "ms",
                description: "Latency of cache operations in milliseconds");
        }

        public async Task<TValue> GetOrAddAsync(TKey key)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                return await _inner.GetOrAddAsync(key);
            }
            finally
            {
                sw.Stop();
                double latency = sw.Elapsed.TotalMilliseconds;
                _operationCounter.Add(1);
                _latencyHistogram.Record(latency);
                _latencyPerKey[key] = latency;
            }
        }

        public TValue GetOrAdd(TKey key)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                return _inner.GetOrAdd(key);
            }
            finally
            {
                sw.Stop();
                double latency = sw.Elapsed.TotalMilliseconds;
                _operationCounter.Add(1);
                _latencyHistogram.Record(latency);
                _latencyPerKey[key] = latency;
            }
        }

        public async Task SetAsync(TKey key, TValue value)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                await _inner.SetAsync(key, value);
            }
            finally
            {
                sw.Stop();
                double latency = sw.Elapsed.TotalMilliseconds;
                _operationCounter.Add(1);
                _latencyHistogram.Record(latency);
                _latencyPerKey[key] = latency;
            }
        }

        /// <summary>
        /// Returns the last recorded latency for the given key, or -1 if none exists.
        /// </summary>
        public double GetLatencyPerKey(TKey key)
        {
            return _latencyPerKey.TryGetValue(key, out var latency) ? latency : -1;
        }

        public int GetEarlyRefreshCount(TKey key) => _inner.GetEarlyRefreshCount(key);
        public int GetGlobalEarlyRefreshCount() => _inner.GetGlobalEarlyRefreshCount();
    }
}
