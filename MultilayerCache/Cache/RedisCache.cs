using StackExchange.Redis;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Polly.CircuitBreaker;
using Polly.Wrap;
using MultilayerCache.Config;

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

        private static readonly MessageParser<TValue> _parser;

        private readonly AsyncPolicy _asyncPolicy;
        private readonly Policy _syncPolicy;

        static RedisCache()
        {
            var parserProp = typeof(TValue).GetProperty("Parser",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
                ?? throw new InvalidOperationException($"Type {typeof(TValue).Name} does not have a static Parser property.");

            _parser = (MessageParser<TValue>)parserProp.GetValue(null)!;
        }

        public RedisCache(string connectionString, ILogger logger, RedisResilienceOptions options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            options ??= new RedisResilienceOptions();

            var redisOptions = ConfigurationOptions.Parse(connectionString);
            redisOptions.AbortOnConnectFail = false;
            _redis = ConnectionMultiplexer.Connect(redisOptions);
            _db = _redis.GetDatabase();

            // Async policies
            var retryPolicy = Policy.Handle<RedisException>()
                .Or<TimeoutException>()
                .WaitAndRetryAsync(
                    options.RetryCount,
                    attempt => TimeSpan.FromMilliseconds(options.RetryDelayMs),
                    (ex, ts) => _logger.LogWarning(ex, "Redis operation failed, retrying in {Delay}", ts)
                );

            var circuitBreakerPolicy = Policy.Handle<RedisException>()
                .Or<TimeoutException>()
                .CircuitBreakerAsync(
                    options.CircuitBreakerFailures,
                    TimeSpan.FromSeconds(options.CircuitBreakerDurationSeconds),
                    onBreak: (ex, ts) => _logger.LogWarning(ex, "Redis circuit open for {Duration}", ts),
                    onReset: () => _logger.LogInformation("Redis circuit closed"),
                    onHalfOpen: () => _logger.LogInformation("Redis circuit half-open")
                );

            _asyncPolicy = Policy.WrapAsync(retryPolicy, circuitBreakerPolicy);

            // Sync policy
            _syncPolicy = Policy.Handle<RedisException>()
                .Or<TimeoutException>()
                .WaitAndRetry(
                    options.RetryCount,
                    attempt => TimeSpan.FromMilliseconds(options.RetryDelayMs),
                    (ex, ts) => _logger.LogWarning(ex, "Redis sync operation failed, retrying in {Delay}", ts)
                );
        }

        public ConnectionMultiplexer Multiplexer => _redis;

        public void Set(TKey key, TValue value, TimeSpan ttl)
        {
            _syncPolicy.Execute(() =>
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
            });
        }

        public async Task SetAsync(TKey key, TValue value, TimeSpan ttl)
        {
            await _asyncPolicy.ExecuteAsync(async () =>
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
            });
        }
        public bool TryGet(TKey key, out TValue value)
        {
            var result = _syncPolicy.Execute(() =>
            {
                try
                {
                    var data = _db.StringGet(key.ToString());
                    if (!data.HasValue)
                    {
                        _logger.LogDebug("Redis cache miss for key {Key}", key);
                        return (false, default(TValue)!);
                    }

                    var parsedValue = _parser.ParseFrom((byte[])data);
                    _logger.LogDebug("Redis cache hit for key {Key}", key);
                    return (true, parsedValue);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading key {Key} from Redis", key);
                    return (false, default(TValue)!);
                }
            });

            value = result.Item2; // <- use Item2 instead of .value
            return result.Item1;   // <- use Item1 instead of .found
        }



        public async Task<(bool, TValue)> TryGetAsync(TKey key)
        {
            var result = await _asyncPolicy.ExecuteAsync(async () =>
            {
                try
                {
                    var data = await _db.StringGetAsync(key.ToString());
                    if (!data.HasValue)
                    {
                        _logger.LogDebug("Redis cache miss for key {Key}", key);
                        return (false, default(TValue)!);
                    }

                    var parsedValue = _parser.ParseFrom((byte[])data);
                    _logger.LogDebug("Redis cache hit for key {Key}", key);
                    return (true, parsedValue);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading key {Key} from Redis", key);
                    return (false, default(TValue)!);
                }
            });

            return result;
        }


        public void Dispose()
        {
            _redis?.Dispose();
        }
    }
}
