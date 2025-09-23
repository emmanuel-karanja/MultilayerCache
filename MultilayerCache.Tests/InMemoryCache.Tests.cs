using Xunit;
using MultilayerCache.Cache;
using MultilayerCache.Demo;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Threading.Tasks;

namespace MultilayerCache.Tests
{
    public class InMemoryCacheTests
    {
        private static InMemoryCache<string, User> CreateCache(TimeSpan? cleanupInterval = null)
        {
            // Use NullLogger to avoid requiring Serilog in tests.
            return new InMemoryCache<string, User>(
                cleanupInterval ?? TimeSpan.FromMinutes(1),
                NullLogger.Instance);
        }

        [Fact]
        public void CanSetAndGetItem()
        {
            var cache = CreateCache();
            var user = new User { Id = 1, Name = "John", Email = "john@example.com" };

            cache.Set("user:1", user, TimeSpan.FromMinutes(5));

            Assert.True(cache.TryGet("user:1", out var cachedUser));
            Assert.Equal("John", cachedUser.Name);
        }

        [Fact]
        public void ExpiredItemReturnsFalse()
        {
            var cache = CreateCache(TimeSpan.FromMilliseconds(50));
            var user = new User { Id = 2, Name = "Jane", Email = "jane@example.com" };
            cache.Set("user:2", user, TimeSpan.FromMilliseconds(50));

            Task.Delay(100).Wait(); // allow expiry

            Assert.False(cache.TryGet("user:2", out _));
        }

        [Fact]
        public async Task CanUseAsyncMethods()
        {
            var cache = CreateCache();
            var user = new User { Id = 3, Name = "Alice", Email = "alice@example.com" };

            await cache.SetAsync("user:3", user, TimeSpan.FromMinutes(5));
            var (found, cachedUser) = await cache.TryGetAsync("user:3");

            Assert.True(found);
            Assert.Equal("Alice", cachedUser.Name);
        }

        [Fact]
        public void MetricsAreTracked()
        {
            var cache = CreateCache();
            var user = new User { Id = 4, Name = "Bob", Email = "bob@example.com" };

            cache.Set("user:4", user, TimeSpan.FromMinutes(1));
            cache.TryGet("user:4", out _);      // Hit
            cache.TryGet("user:unknown", out _); // Miss

            Assert.Equal(1, cache.Metrics.Hits);
            Assert.Equal(1, cache.Metrics.Misses);
        }
    }
}
