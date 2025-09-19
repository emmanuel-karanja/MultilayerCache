using StackExchange.Redis;
using System;
using System.Threading.Tasks;
using Google.Protobuf;

/*This uses Redis as cache backbone and protobuf messages for latency i.e. protobuf is more compact,
I have to see what latency serial/deserial adds.*/
namespace MultilayerCache.Cache
{
    public class RedisCache<TKey, TValue> : ICache<TKey, TValue> where TValue : IMessage<TValue>
    {
        private readonly IDatabase _db;

        public RedisCache(string connectionString)
        {
            var redis = ConnectionMultiplexer.Connect(connectionString);
            _db = redis.GetDatabase();
        }

        public void Set(TKey key, TValue value, TimeSpan ttl)
        {
            var data = ProtobufSerializer.Serialize(value);
            _db.StringSet(key?.ToString(), data, ttl);
        }

        public bool TryGet(TKey key, out TValue value)
        {
            var data = _db.StringGet(key?.ToString());
            if (!data.HasValue)
            {
                value = default!;
                return false;
            }

            // Use the MessageParser from the TValue type
            value = ParseFromBytes(data);
            return true;
        }

        private static TValue ParseFromBytes(RedisValue data)
        {
            // TValue must have a static Parser property
            var parserProperty = typeof(TValue).GetProperty("Parser", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            if (parserProperty == null)
                throw new InvalidOperationException($"Type {typeof(TValue).Name} does not have a static Parser property.");

            var parser = (MessageParser<TValue>)parserProperty.GetValue(null)!;
            return parser.ParseFrom((byte[])data);
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
    }
}
