using StackExchange.Redis;

namespace PulseFlow.Api.Ingestion.RateLimiting;

public sealed class RedisConnectionState
{
    private IConnectionMultiplexer? _connectionMultiplexer;

    public IConnectionMultiplexer? ConnectionMultiplexer => Volatile.Read(ref _connectionMultiplexer);

    public void SetConnectionMultiplexer(IConnectionMultiplexer connectionMultiplexer)
    {
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);
        Interlocked.CompareExchange(ref _connectionMultiplexer, connectionMultiplexer, null);
    }
}
