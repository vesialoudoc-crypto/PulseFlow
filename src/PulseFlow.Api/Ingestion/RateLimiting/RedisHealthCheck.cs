using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PulseFlow.Api.Ingestion.RateLimiting;

public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly RedisConnectionState _connectionState;

    public RedisHealthCheck(RedisConnectionState connectionState)
    {
        _connectionState = connectionState;
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
            throw;
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Redis is unavailable.", exception);
        }
    }
}
