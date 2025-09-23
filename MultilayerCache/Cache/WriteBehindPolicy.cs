namespace MultilayerCache.Cache;
public class WriteBehindPolicy<TKey, TValue> : IWritePolicy<TKey, TValue>
{
    private readonly TimeSpan _ttl;
    public WriteBehindPolicy(TimeSpan ttl) => _ttl = ttl;

    public async Task WriteAsync(TKey key, TValue value, ICache<TKey, TValue>[] layers, ILogger logger)
    {
        // Write only to the first (fastest) layer synchronously
        try
        {
            await layers[0].SetAsync(key, value, _ttl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Write-behind failed for key {Key} in top layer", key);
        }

        // Propagate asynchronously to other layers
        _ = Task.Run(async () =>
        {
            for (int i = 1; i < layers.Length; i++)
            {
                try
                {
                    await layers[i].SetAsync(key, value, _ttl);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Write-behind failed for key {Key} in layer {Layer}", key, i);
                }
            }
        });
    }
}
