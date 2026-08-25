using PulseFlow.Api.Startup;

namespace PulseFlow.Api.Ingestion.Messaging.RabbitMq;

internal sealed class RabbitMqMessagingInitializer : IStartupInitializer
{
    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly RabbitMqIngestionBatchPublisher _publisher;

    public RabbitMqMessagingInitializer(
        RabbitMqConnectionManager connectionManager,
        RabbitMqIngestionBatchPublisher publisher
    )
    {
        _connectionManager = connectionManager;
        _publisher = publisher;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        // Topology and the publisher pool are established once before the API is ready.
        await _connectionManager.InitializeAsync(ct);
        await _publisher.InitializeAsync(ct);
    }
}
