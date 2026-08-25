using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PulseFlow.Api.Ingestion.Messaging.RabbitMq;

internal sealed class RabbitMqHealthCheck : IHealthCheck
{
    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly RabbitMqIngestionBatchPublisher _publisher;

    public RabbitMqHealthCheck(RabbitMqConnectionManager connectionManager, RabbitMqIngestionBatchPublisher publisher)
    {
        _connectionManager = connectionManager;
        _publisher = publisher;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var result =
            _connectionManager.IsConnected && _publisher.HasUsableChannel
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("RabbitMQ is unavailable or has no usable publisher channels.");

        return Task.FromResult(result);
    }
}
