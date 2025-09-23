namespace MultilayerCache.Config
{
   public class RedisResilienceOptions
{
    public int RetryCount { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 50;

    public int CircuitBreakerFailures { get; set; } = 5;
    public int CircuitBreakerDurationSeconds { get; set; } = 30;
}
 
}
