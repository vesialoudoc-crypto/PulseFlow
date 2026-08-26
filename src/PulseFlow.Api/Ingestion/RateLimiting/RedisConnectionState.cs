using StackExchange.Redis;

namespace PulseFlow.Api.Ingestion.RateLimiting;

public sealed class RedisConnectionState
{
    private IConnectionMultiplexer? _connectionMultiplexer;

    public IConnectionMultiplexer? ConnectionMultiplexer => Volatile.Read(ref _connectionMultiplexer);

    public void SetConnectionMultiplexer(IConnectionMultiplexer connectionMultiplexer)
    {
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);

        if (Interlocked.CompareExchange(ref _connectionMultiplexer, connectionMultiplexer, null) is not null)
        {
            throw new InvalidOperationException("Redis connection has already been established.");
        }
    }

    public IConnectionMultiplexer GetRequiredConnectionMultiplexer()
    {
        return ConnectionMultiplexer
            ?? throw new InvalidOperationException("Redis startup initialization has not established a connection.");
    }
}
