using StackExchange.Redis;
using System;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace MultilayerCache.Cache
{
    public class RedisCache<TKey, TValue> : ICache<TKey, TValue>, IDisposable
        where TValue : IMessage<TValue> 
        where TKey: notnull
    {
        private readonly IDatabase _db;
        private readonly ConnectionMultiplexer _redis;
        private readonly ILogger _logger;

        public RedisCache(string connectionString, ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _redis = ConnectionMultiplexer.Connect(connectionString);
            _db = _redis.GetDatabase();
        }

        // Expose multiplexer for metrics/monitoring
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

                var parserProp = typeof(TValue).GetProperty("Parser",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                var parser = (MessageParser<TValue>)parserProp!.GetValue(null)!;
                value = parser.ParseFrom((byte[])data);

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

        public Task SetAsync(TKey key, TValue value, TimeSpan ttl)
        {
            Set(key, value, ttl);
            return Task.CompletedTask;
        }

        public Task<(bool found, TValue value)> TryGetAsync(TKey key)
        {
            var found = TryGet(key, out var value);
            return Task.FromResult((found, value));
        }

        public void Dispose()
        {
            _redis?.Dispose();
        }
    }
}
