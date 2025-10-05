using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.Concurrent;

namespace MultilayerCache.Cache
{
    /// <summary>
    /// Decorator for IMultilayerCacheManager that adds instrumentation for metrics.
    /// Measures latency, throughput, operation counts, and exposes raw cache metrics.
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

        public async Task<TValue> GetOrAddAsync(TKey key, CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                return await _inner.GetOrAddAsync(key, token);
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

        /// <summary>
        /// Returns the top N most frequently accessed keys and their counts.
        /// Delegates to the inner cache manager.
        /// </summary>
        public (TKey Key, int Count)[] GetTopKeys(int n)
        {
            if (_inner is MultilayerCacheManager<TKey, TValue> concrete)
            {
                var snapshot = concrete.GetMetricsSnapshot(n);
                var topKeys = snapshot.TopKeysByAccessCount
                    .Select(k => (Key: k, Count: snapshot.HitsPerKey.TryGetValue(k, out var c) ? c : 0))
                    .ToArray();
                return topKeys;
            }
            return Array.Empty<(TKey, int)>();
        }

       
        /// Returns a snapshot of the cache metrics from the inner cache manager, plus latency data.
        /// </summary>
        public CacheMetricsSnapshot<TKey> GetMetricsSnapshot(int topN = 10)
        {
            var snapshot = _inner.GetMetricsSnapshot(topN);

            // Add last recorded latency per key from this decorator
            snapshot.LastLatencyPerKey = new Dictionary<TKey, double>(_latencyPerKey);

            return snapshot;
        }
    }
}
