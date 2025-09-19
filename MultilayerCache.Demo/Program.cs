using MultilayerCache.Cache;
using MultilayerCache.Demo;
class Program
{
    static async Task Main()
    {
        // Setup caches
        var memoryCache = new InMemoryCache<string, User>(TimeSpan.FromMinutes(1));
        var redisCache = new RedisCache<string, User>("localhost"); // Adjust connection string if needed

        // Multilayer cache manager
        var cache = new MultilayerCacheManager<string, User>(memoryCache, redisCache);

        // Sample user
        var user = new User { Id = 1, Name = "John Doe", Email = "john@example.com" };

        // Set in cache
        await cache.SetAsync("user:1", user, TimeSpan.FromMinutes(5));

        // Try to get immediately
        var (found, cachedUser) = await cache.TryGetAsync("user:1");
        Console.WriteLine(found ? $"Cache Hit: {cachedUser.Name}" : "Cache Miss");

        // Show memory cache metrics
        Console.WriteLine($"Memory Cache Hits: {memoryCache.Metrics.Hits}, Misses: {memoryCache.Metrics.Misses}");

        // Wait for memory cache to expire
        Console.WriteLine("Waiting 6 minutes for memory cache to expire...");
        await Task.Delay(TimeSpan.FromMinutes(6));

        // Try to get again (should hit Redis)
        var (foundAfter, userAfter) = await cache.TryGetAsync("user:1");
        Console.WriteLine(foundAfter ? $"Cache Hit (Redis fallback): {userAfter.Name}" : "Cache Miss");

        // Show updated memory cache metrics
        Console.WriteLine($"Memory Cache Hits: {memoryCache.Metrics.Hits}, Misses: {memoryCache.Metrics.Misses}");
    }
}
