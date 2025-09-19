using StackExchange.Redis;
using System;
using System.Threading.Tasks;
using Google.Protobuf;

namespace MultilayerCache.Cache
{
public class RedisCache<TKey, TValue> : ICache<TKey, TValue>
where TValue : IMessage<TValue>
{
    private readonly IDatabase _db;
    private readonly ConnectionMultiplexer _redis;

    public RedisCache(string connectionString)
    {
        _redis = ConnectionMultiplexer.Connect(connectionString);
        _db = _redis.GetDatabase();
    }

    // Expose the multiplexer for metrics
    public ConnectionMultiplexer Multiplexer => _redis;

    public void Set(TKey key, TValue value, TimeSpan ttl)
    {
        var data = ProtobufSerializer.Serialize(value); // TValue is always IMessage<TValue>
        _db.StringSet(key.ToString(), data, ttl);
    }

    public bool TryGet(TKey key, out TValue value)
    {
        var data = _db.StringGet(key.ToString());
        if (!data.HasValue)
        {
            value = default!;
            return false;
        }

        var parserProp = typeof(TValue).GetProperty("Parser",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
        var parser = (MessageParser<TValue>)parserProp!.GetValue(null)!;
        value = parser.ParseFrom((byte[])data);

        return true;
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
