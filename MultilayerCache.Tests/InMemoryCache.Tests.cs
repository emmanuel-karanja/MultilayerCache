using Xunit;
using MultilayerCache.Cache;
using MultilayerCache.Demo;

namespace MultilayerCache.Tests
{
    public class InMemoryCacheTests
    {
        [Fact]
        public void CanSetAndGetItem()
        {
            var cache = new InMemoryCache<string, User>(TimeSpan.FromMinutes(1));
            var user = new User { Id = 1, Name = "John", Email = "john@example.com" };
            cache.Set("user:1", user, TimeSpan.FromMinutes(5));

            Assert.True(cache.TryGet("user:1", out var cachedUser));
            Assert.Equal("John", cachedUser.Name);
        }

        [Fact]
        public void ExpiredItemReturnsFalse()
        {
            var cache = new InMemoryCache<string, User>(TimeSpan.FromMilliseconds(50));
            var user = new User { Id = 2, Name = "Jane", Email = "jane@example.com" };
            cache.Set("user:2", user, TimeSpan.FromMilliseconds(50));

            Task.Delay(100).Wait();

            Assert.False(cache.TryGet("user:2", out _));
        }

        [Fact]
        public async Task CanUseAsyncMethods()
        {
            var cache = new InMemoryCache<string, User>(TimeSpan.FromMinutes(1));
            var user = new User { Id = 3, Name = "Alice", Email = "alice@example.com" };

            await cache.SetAsync("user:3", user, TimeSpan.FromMinutes(5));
            var (found, cachedUser) = await cache.TryGetAsync("user:3");

            Assert.True(found);
            Assert.Equal("Alice", cachedUser.Name);
        }

        [Fact]
        public void MetricsAreTracked()
        {
            var cache = new InMemoryCache<string, User>(TimeSpan.FromMinutes(1));
            var user = new User { Id = 4, Name = "Bob", Email = "bob@example.com" };

            cache.Set("user:4", user, TimeSpan.FromMinutes(1));
            cache.TryGet("user:4", out _);  // Hit
            cache.TryGet("user:unknown", out _);  // Miss

            Assert.Equal(1, cache.Metrics.Hits);
            Assert.Equal(1, cache.Metrics.Misses);
        }
    }
}
