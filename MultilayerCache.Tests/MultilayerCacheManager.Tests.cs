using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using MultilayerCache.Cache;
using Xunit;

namespace MultilayerCache.Tests
{
    public class MultilayerCacheManagerTests
    {
        private class TestCache : ICache<string, string>
        {
            private readonly Dictionary<string, string> _store = new();

            // Async methods
            public Task<(bool, string)> TryGetAsync(string key)
            {
                return Task.FromResult(_store.TryGetValue(key, out var val) ? (true, val) : (false, null!));
            }

            public Task SetAsync(string key, string value, TimeSpan ttl)
            {
                _store[key] = value;
                return Task.CompletedTask;
            }

            // Synchronous methods (implement interface)
            public bool TryGet(string key, out string value)
            {
                return _store.TryGetValue(key, out value!);
            }

            public void Set(string key, string value, TimeSpan ttl)
            {
                _store[key] = value;
            }
        }


        [Fact]
        public async Task GetOrAddAsync_ReturnsLoaderValue_OnCacheMiss()
        {
            var mem = new TestCache();
            var redis = new TestCache();
            var logger = Mock.Of<ILogger>();

            var loaderCalled = false;
            Func<string, CancellationToken, Task<string>> loader = async (k, t) =>
            {
                loaderCalled = true;
                await Task.Delay(1);
                return "value_from_loader";
            };

            var mgr = new MultilayerCacheManager<string, string>(
                new ICache<string, string>[] { mem, redis },
                loader,
                logger);

            var value = await mgr.GetOrAddAsync("key1");
            Assert.Equal("value_from_loader", value);
            Assert.True(loaderCalled);

            // Verify it is written to memory and redis caches
            var (foundMem, valMem) = await mem.TryGetAsync("key1");
            Assert.True(foundMem);
            Assert.Equal("value_from_loader", valMem);

            var (foundRedis, valRedis) = await redis.TryGetAsync("key1");
            Assert.True(foundRedis);
            Assert.Equal("value_from_loader", valRedis);
        }

        [Fact]
        public async Task GetOrAddAsync_ReturnsCachedValue_OnCacheHit()
        {
            var mem = new TestCache();
            var redis = new TestCache();
            var logger = Mock.Of<ILogger>();

            await mem.SetAsync("key1", "cached_value", TimeSpan.FromMinutes(5));

            var loaderCalled = false;
            Func<string, CancellationToken, Task<string>> loader = (k, t) =>
            {
                loaderCalled = true;
                return Task.FromResult("loader_value");
            };

            var mgr = new MultilayerCacheManager<string, string>(
                new ICache<string, string>[] { mem, redis },
                loader,
                logger);

            var value = await mgr.GetOrAddAsync("key1");
            Assert.Equal("cached_value", value);
            Assert.False(loaderCalled);
        }

        [Fact]
        public async Task PromotionPolicy_FirstLayerOnly_PromotesToFirstLayerOnly()
        {
            var layer0 = new TestCache();
            var layer1 = new TestCache();
            var logger = Mock.Of<ILogger>();

            await layer1.SetAsync("key1", "value_from_layer1", TimeSpan.FromMinutes(5));

            var mgr = new MultilayerCacheManager<string, string>(
                new ICache<string, string>[] { layer0, layer1 },
                (k, t) => Task.FromResult("loader_value"),
                logger,
                promotionPolicy: PromotionPolicy.FirstLayerOnly);

            var value = await mgr.GetOrAddAsync("key1");

            var (found0, val0) = await layer0.TryGetAsync("key1");
            Assert.True(found0);
            Assert.Equal("value_from_layer1", val0);

            var (found1, val1) = await layer1.TryGetAsync("key1");
            Assert.True(found1);
            Assert.Equal("value_from_layer1", val1);
        }

        [Fact]
        public async Task RequestCoalescing_AllowsOnlyOneLoaderCall()
        {
            var mem = new TestCache();
            var redis = new TestCache();
            var logger = Mock.Of<ILogger>();

            int loaderCalls = 0;
            Func<string, CancellationToken, Task<string>> loader = async (k, t) =>
            {
                Interlocked.Increment(ref loaderCalls);
                await Task.Delay(50); // simulate slow load
                return "loaded";
            };

            var mgr = new MultilayerCacheManager<string, string>(
                new ICache<string, string>[] { mem, redis },
                loader,
                logger);

            var tasks = new List<Task<string>>();
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(mgr.GetOrAddAsync("key1"));
            }

            var results = await Task.WhenAll(tasks);
            Assert.All(results, v => Assert.Equal("loaded", v));
            Assert.Equal(1, loaderCalls);
        }

        [Fact]
        public async Task EarlyRefresh_IncrementsEarlyRefreshCount()
        {
            var mem = new TestCache();
            var redis = new TestCache();
            var logger = Mock.Of<ILogger>();

            int loaderCalls = 0;
            Func<string, CancellationToken, Task<string>> loader = async (k, t) =>
            {
                Interlocked.Increment(ref loaderCalls);
                return "refreshed";
            };

            var mgr = new MultilayerCacheManager<string, string>(
                new ICache<string, string>[] { mem, redis },
                loader,
                logger,
                defaultTtl: TimeSpan.FromMilliseconds(50),
                earlyRefreshThreshold: TimeSpan.FromMilliseconds(10),
                minRefreshInterval: TimeSpan.Zero);

            // Seed value
            await mgr.SetAsync("key1", "initial");

            // Wait for TTL + threshold to trigger early refresh
            await Task.Delay(60);

            // Force access to trigger early refresh
            var val = await mgr.GetOrAddAsync("key1");

            // Allow background refresh to run
            await Task.Delay(100);

            var earlyCount = mgr.GetEarlyRefreshCount("key1");
            Assert.True(earlyCount > 0);
            Assert.Equal("refreshed", val); // latest value
        }

        [Fact]
        public void GetOrAdd_Synchronous_Works()
        {
            var mem = new TestCache();
            var redis = new TestCache();
            var logger = Mock.Of<ILogger>();

            Func<string, CancellationToken, Task<string>> loader = (k, t) => Task.FromResult("sync_value");

            var mgr = new MultilayerCacheManager<string, string>(
                new ICache<string, string>[] { mem, redis },
                loader,
                logger);

            var value = mgr.GetOrAdd("key_sync");
            Assert.Equal("sync_value", value);
        }
    }
}
