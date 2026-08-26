using Microsoft.Extensions.Options;
using PulseFlow.Api.Startup;
using StackExchange.Redis;

namespace PulseFlow.Api.Ingestion.RateLimiting;

public sealed class RedisStartupInitializer : IStartupInitializer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly RedisConnectionMultiplexerFactory _connectionMultiplexerFactory;
    private readonly RedisConnectionState _connectionState;
    private readonly RedisOptions _options;
    private readonly ILogger<RedisStartupInitializer> _logger;

    public RedisStartupInitializer(
        IServiceProvider serviceProvider,
        RedisConnectionMultiplexerFactory connectionMultiplexerFactory,
        RedisConnectionState connectionState,
        IOptions<RedisOptions> options,
        ILogger<RedisStartupInitializer> logger
    )
    {
        _serviceProvider = serviceProvider;
        _connectionMultiplexerFactory = connectionMultiplexerFactory;
        _connectionState = connectionState;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var connectionTask = _connectionMultiplexerFactory.ConnectAsync();
        var connectionWasEstablished = false;

        try
        {
            var connectionMultiplexer = await connectionTask.WaitAsync(ct);
            ct.ThrowIfCancellationRequested();

            _connectionState.SetConnectionMultiplexer(connectionMultiplexer);
            connectionWasEstablished = true;

            // Resolving the limiter loads its embedded Lua script before the first ingestion request.
            _ = _serviceProvider.GetRequiredService<IIngestionRateLimiter>();
            await connectionMultiplexer.GetDatabase().PingAsync().WaitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (!connectionWasEstablished)
            {
                _ = DisposeConnectionWhenCompletedAsync(connectionTask);
            }

            throw new OperationCanceledException(ct);
        }
        catch (RedisTimeoutException exception)
        {
            _logger.LogWarning(
                exception,
                "Redis startup ping timed out after {ConfiguredTimeout}.",
                _options.AsyncTimeout
            );
            throw;
        }
    }

    private async Task DisposeConnectionWhenCompletedAsync(Task<IConnectionMultiplexer> connectionTask)
    {
        try
        {
            var connectionMultiplexer = await connectionTask;
            connectionMultiplexer.Dispose();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Redis connection attempt completed unsuccessfully after startup cancellation."
            );
        }
    }
}
