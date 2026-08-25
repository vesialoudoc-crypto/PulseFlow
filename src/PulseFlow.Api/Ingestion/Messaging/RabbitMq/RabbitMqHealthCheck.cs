using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PulseFlow.Api.Ingestion.Messaging.RabbitMq;

internal sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly RabbitMqConnectionManager _connectionManager;

    public RabbitMqHealthCheck(RabbitMqConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var result = _connectionManager.IsConnected
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("RabbitMQ is unavailable.");

        return Task.FromResult(result);
    }
}
