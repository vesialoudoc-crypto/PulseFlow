using Microsoft.Extensions.Hosting;

namespace PulseFlow.Api.Ingestion.Messaging.RabbitMq;

internal sealed class RabbitMqMessagingInitializer : IHostedService
{
    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly RabbitMqIngestionBatchPublisher _publisher;

    public RabbitMqMessagingInitializer(
        RabbitMqConnectionManager connectionManager,
        RabbitMqIngestionBatchPublisher publisher)
    {
        _connectionManager = connectionManager;
        _publisher = publisher;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Make broker problems visible before the API starts taking traffic.
        await _connectionManager.InitializeAsync(cancellationToken);
        await _publisher.InitializeAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
