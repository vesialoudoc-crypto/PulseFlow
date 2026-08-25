using PulseFlow.Api.Startup;
using StackExchange.Redis;

namespace PulseFlow.Api.Ingestion.RateLimiting;

public sealed class RedisStartupInitializer : IStartupInitializer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly RedisConnectionState _connectionState;

    public RedisStartupInitializer(IServiceProvider serviceProvider, RedisConnectionState connectionState)
    {
        _serviceProvider = serviceProvider;
        _connectionState = connectionState;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Resolving the limiter loads its embedded Lua script before the first ingestion request.
        _ = _serviceProvider.GetRequiredService<IIngestionRateLimiter>();
        var connectionMultiplexer = _serviceProvider.GetRequiredService<IConnectionMultiplexer>();
        _connectionState.SetConnectionMultiplexer(connectionMultiplexer);
        await connectionMultiplexer.GetDatabase().PingAsync().WaitAsync(ct);
    }
}
