namespace MultilayerCache.Cache;
public class WriteThroughPolicy<TKey, TValue> : IWritePolicy<TKey, TValue>
{
    private readonly TimeSpan _ttl;
    public WriteThroughPolicy(TimeSpan ttl) => _ttl = ttl;

    public async Task WriteAsync(TKey key, TValue value, ICache<TKey, TValue>[] layers, ILogger logger)
    {
        foreach (var layer in layers)
        {
            try
            {
                await layer.SetAsync(key, value, _ttl);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Write-through failed for key {Key} in layer {Layer}", key, layer.GetType().Name);
            }
        }
    }
}
