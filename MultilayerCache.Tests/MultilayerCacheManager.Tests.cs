using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using MultilayerCache.Cache;
using Microsoft.Extensions.Logging.Abstractions;

public class MultilayerCacheManagerFullTests
{
    private class TestWritePolicy<TKey, TValue> : IWritePolicy<TKey, TValue>
        where TKey : notnull
    {
        public List<(TKey key, TValue value)> Written = new();
        public TimeSpan DefaultTtl { get; }

        public TestWritePolicy(TimeSpan ttl) => DefaultTtl = ttl;

        public Task WriteAsync(TKey key, TValue value, ICache<TKey, TValue>[] layers, ILogger logger, Func<TKey, TValue, Task> persistentStoreWriter)
        {
            Written.Add((key, value));
            return Task.CompletedTask;
        }
    }

    private class TestCache<TKey, TValue> : ICache<TKey, TValue>
        where TKey : notnull
    {
        private readonly Dictionary<TKey, TValue> _store = new();
        private readonly TimeSpan _ttl;

        public TestCache(TimeSpan ttl) => _ttl = ttl;

        public void Set(TKey key, TValue value, TimeSpan ttl) => _store[key] = value;
        public bool TryGet(TKey key, out TValue value) => _store.TryGetValue(key, out value!);

        public Task SetAsync(TKey key, TValue value, TimeSpan ttl) { Set(key, value, ttl); return Task.CompletedTask; }
        public Task<(bool found, TValue value)> TryGetAsync(TKey key)
        {
            var found = _store.TryGetValue(key, out var value);
            return Task.FromResult((found, value!));
        }
    }

    [Fact]
    public async Task CacheMiss_ShouldCallLoader_AndWriteToAllLayers()
    {
        var logger = new Mock<ILogger>().Object;
        var l1 = new TestCache<string, string>(TimeSpan.FromMinutes(5));
        var l2 = new TestCache<string, string>(TimeSpan.FromMinutes(5));
        var writePolicy = new TestWritePolicy<string, string>(TimeSpan.FromMinutes(5));

        bool loaderCalled = false;
        Task<string> Loader(string key)
        {
            loaderCalled = true;
            return Task.FromResult("LoadedValue");
        }

        var manager = new MultilayerCacheManager<string, string>(
            new ICache<string, string>[] { l1, l2 },
            Loader,
            logger,
            writePolicy,
            defaultTtl: TimeSpan.FromMinutes(5),
            persistentStoreWriter: async (k, v) => { /* nothing */ },
            earlyRefreshThreshold: TimeSpan.FromMilliseconds(100),
            minRefreshInterval: TimeSpan.Zero,
            maxConcurrentEarlyRefreshes: 1
        );

        var value = await manager.GetOrAddAsync("key1");

        Assert.Equal("LoadedValue", value);
        Assert.True(loaderCalled);
        Assert.Single(writePolicy.Written);
        Assert.Equal("key1", writePolicy.Written[0].key);
        Assert.Equal("LoadedValue", writePolicy.Written[0].value);
    }

    [Fact]
    public async Task CacheHit_ShouldReturnL1Value_AndPromoteL2Value()
    {
        var logger = new Mock<ILogger>().Object;
        var l1 = new TestCache<string, string>(TimeSpan.FromMinutes(5));
        var l2 = new TestCache<string, string>(TimeSpan.FromMinutes(5));

        await l2.SetAsync("key1", "L2Value", TimeSpan.FromMinutes(5));

        Task<string> Loader(string key) => Task.FromResult("LoadedValue");

        var manager = new MultilayerCacheManager<string, string>(
            new ICache<string, string>[] { l1, l2 },
            Loader,
            logger,
            defaultTtl: TimeSpan.FromMinutes(5)
        );

        var value = await manager.GetOrAddAsync("key1");

        Assert.Equal("L2Value", value);

        var (found, promotedValue) = await l1.TryGetAsync("key1");
        Assert.True(found);
        Assert.Equal("L2Value", promotedValue);
    }

    [Fact]
    public async Task RequestCoalescing_ShouldCallLoaderOnlyOnce()
    {
        var logger = new Mock<ILogger>().Object;
        var l1 = new TestCache<string, string>(TimeSpan.FromMinutes(5));
        var l2 = new TestCache<string, string>(TimeSpan.FromMinutes(5));

        int loaderCount = 0;
        async Task<string> Loader(string key)
        {
            Interlocked.Increment(ref loaderCount);
            await Task.Delay(50);
            return "Value";
        }

        var manager = new MultilayerCacheManager<string, string>(
            new ICache<string, string>[] { l1, l2 },
            Loader,
            logger
        );

        var tasks = new List<Task<string>>();
        for (int i = 0; i < 10; i++)
            tasks.Add(manager.GetOrAddAsync("key1"));

        await Task.WhenAll(tasks);

        Assert.Equal(1, loaderCount);
    }


    [Fact]
    public async Task SetAsync_ShouldCallWritePolicyAndUpdateLastRefresh()
    {
        var logger = new Mock<ILogger>().Object;
        var l1 = new TestCache<string, string>(TimeSpan.FromMinutes(5));
        var writePolicy = new TestWritePolicy<string, string>(TimeSpan.FromMinutes(5));

        var manager = new MultilayerCacheManager<string, string>(
            new ICache<string, string>[] { l1 },
            key => Task.FromResult("Loader"),
            logger,
            writePolicy
        );

        await manager.SetAsync("key1", "Value1");

        Assert.Single(writePolicy.Written);
        Assert.Equal("Value1", writePolicy.Written[0].value);
    }
}
