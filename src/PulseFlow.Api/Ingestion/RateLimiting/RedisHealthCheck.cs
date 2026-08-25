using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PulseFlow.Api.Ingestion.RateLimiting;

public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly RedisConnectionState _connectionState;
    private readonly ILogger<RedisHealthCheck> _logger;

    public RedisHealthCheck(RedisConnectionState connectionState, ILogger<RedisHealthCheck> logger)
    {
        _connectionState = connectionState;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var connectionMultiplexer = _connectionState.ConnectionMultiplexer;
            if (connectionMultiplexer is null)
            {
                return HealthCheckResult.Unhealthy("Redis startup initialization has not established a connection.");
            }

            await connectionMultiplexer.GetDatabase().PingAsync().WaitAsync(ct);
            return HealthCheckResult.Healthy();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Redis readiness check timed out.");
            return HealthCheckResult.Unhealthy("Redis is unavailable.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Redis readiness check failed.");
            return HealthCheckResult.Unhealthy("Redis is unavailable.");
        }
    }
}
