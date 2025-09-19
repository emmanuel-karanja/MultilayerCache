using MultilayerCache.Cache;
using MultilayerCache.Demo;
using System;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        var random = new Random();

        // Setup caches
        var memoryCache = new InMemoryCache<string, User>(TimeSpan.FromSeconds(30)); // short TTL
        var redisCache = new RedisCache<string, User>("localhost,allowAdmin=true");
        var cache = new MultilayerCacheManager<string, User>(memoryCache, redisCache);

        const int totalItems = 10_000;
        int redisFallbacks = 0; // track L2 retrievals

        Console.WriteLine($"Caching {totalItems:N0} users...");

        // Insert 10,000 users
        for (int i = 1; i <= totalItems; i++)
        {
            var user = new User
            {
                Id = i,
                Name = $"User {i}",
                Email = $"user{i}@example.com"
            };

            await cache.SetAsync($"user:{i}", user, TimeSpan.FromMinutes(5));

            if (i % 1000 == 0)
                Console.WriteLine($"{i:N0} users cached...");
        }

        Console.WriteLine("✅ All users cached.");

        // Access immediately: should mostly be L1
        Console.WriteLine("Accessing 2000 random users immediately (L1 hits expected)...");
        for (int j = 0; j < 2000; j++)
        {
            int id = random.Next(1, totalItems + 1);
            var (found, user) = await cache.TryGetAsync($"user:{id}");
        }

        // Wait for L1 to expire
        Console.WriteLine("Waiting 40s for memory cache to expire...");
        await Task.Delay(TimeSpan.FromSeconds(40));

        // Access again: should hit Redis (L2)
        Console.WriteLine("Accessing 2000 random users after memory expiration (L2 hits expected)...");
        for (int j = 0; j < 2000; j++)
        {
            int id = random.Next(1, totalItems + 1);
            var (found, user) = await cache.TryGetAsync($"user:{id}");
            if (found) redisFallbacks++;
        }

        // Misses from fake keys
        Console.WriteLine("Accessing 500 random *non-existent* users...");
        for (int j = 0; j < 500; j++)
        {
            int fakeId = totalItems + random.Next(1, 1000);
            var (found, user) = await cache.TryGetAsync($"user:{fakeId}");
        }

        Console.WriteLine();
        Console.WriteLine("---- Metrics ----");
        Console.WriteLine($"Memory Cache (L1) Hits: {memoryCache.Metrics.Hits}, Misses: {memoryCache.Metrics.Misses}");
        Console.WriteLine($"Redis Cache   (L2) Approx Hits (fallbacks after L1 miss): {redisFallbacks}");
    }
}
