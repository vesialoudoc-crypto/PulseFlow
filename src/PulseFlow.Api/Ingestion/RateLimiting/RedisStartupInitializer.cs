using Microsoft.Extensions.Options;
using PulseFlow.Api.Startup;
using StackExchange.Redis;

namespace PulseFlow.Api.Ingestion.RateLimiting;

public sealed class RedisStartupInitializer : IStartupInitializer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly RedisConnectionState _connectionState;
    private readonly RedisOptions _options;
    private readonly ILogger<RedisStartupInitializer> _logger;

    public RedisStartupInitializer(
        IServiceProvider serviceProvider,
        RedisConnectionState connectionState,
        IOptions<RedisOptions> options,
        ILogger<RedisStartupInitializer> logger
    )
    {
        _serviceProvider = serviceProvider;
        _connectionState = connectionState;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var connectionMultiplexer = _serviceProvider.GetRequiredService<IConnectionMultiplexer>();
        _connectionState.SetConnectionMultiplexer(connectionMultiplexer);

        // Resolving the limiter loads its embedded Lua script before the first ingestion request.
        _ = _serviceProvider.GetRequiredService<IIngestionRateLimiter>();
        try
        {
            await connectionMultiplexer.GetDatabase().PingAsync().WaitAsync(_options.AsyncTimeout, ct);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Redis startup ping timed out after {ConfiguredTimeout}.", _options.AsyncTimeout);
            throw;
        }
    }
}
