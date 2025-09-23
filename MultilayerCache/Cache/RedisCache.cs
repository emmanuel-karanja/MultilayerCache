using StackExchange.Redis;
using System;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace MultilayerCache.Cache
{
    public class RedisCache<TKey, TValue> : ICache<TKey, TValue>, IDisposable
        where TValue : IMessage<TValue>
        where TKey : notnull
    {
        private readonly IDatabase _db;
        private readonly ConnectionMultiplexer _redis;
        private readonly ILogger _logger;

        public CacheMetrics Metrics { get; } = new();

        // Cached static parser to avoid reflection overhead
        private static readonly MessageParser<TValue> _parser;

        static RedisCache()
        {
            var parserProp = typeof(TValue).GetProperty("Parser",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
                ?? throw new InvalidOperationException($"Type {typeof(TValue).Name} does not have a static Parser property.");

            _parser = (MessageParser<TValue>)parserProp.GetValue(null)!;
        }

        public RedisCache(string connectionString, ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _redis = ConnectionMultiplexer.Connect(connectionString);
            _db = _redis.GetDatabase();
        }

        public ConnectionMultiplexer Multiplexer => _redis;

        public void Set(TKey key, TValue value, TimeSpan ttl)
        {
            try
            {
                var data = ProtobufSerializer.Serialize(value);
                _db.StringSet(key.ToString(), data, ttl);
                _logger.LogDebug("Set key {Key} in Redis with TTL {TTL}", key, ttl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to set key {Key} in Redis", key);
            }
        }

        public async Task SetAsync(TKey key, TValue value, TimeSpan ttl)
        {
            try
            {
                var data = ProtobufSerializer.Serialize(value);
                await _db.StringSetAsync(key.ToString(), data, ttl);
                _logger.LogDebug("Async set key {Key} in Redis with TTL {TTL}", key, ttl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to async set key {Key} in Redis", key);
            }
        }

        public bool TryGet(TKey key, out TValue value)
        {
            try
            {
                var data = _db.StringGet(key.ToString());
                if (!data.HasValue)
                {
                    _logger.LogDebug("Redis cache miss for key {Key}", key);
                    value = default!;
                    return false;
                }

                value = _parser.ParseFrom((byte[])data);
                _logger.LogDebug("Redis cache hit for key {Key}", key);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading key {Key} from Redis", key);
                value = default!;
                return false;
            }
        }

        public async Task<(bool found, TValue value)> TryGetAsync(TKey key)
        {
            try
            {
                var data = await _db.StringGetAsync(key.ToString());
                if (!data.HasValue)
                {
                    _logger.LogDebug("Redis cache miss for key {Key}", key);
                    return (false, default!);
                }

                var value = _parser.ParseFrom((byte[])data);
                _logger.LogDebug("Redis cache hit for key {Key}", key);
                return (true, value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading key {Key} from Redis", key);
                return (false, default!);
            }
        }

        public void Dispose()
        {
            _redis?.Dispose();
        }
    }
}
