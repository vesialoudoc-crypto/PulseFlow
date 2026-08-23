namespace PulseFlow.Api.Ingestion.Messaging.RabbitMq;

internal sealed class RabbitMqIngestionBatchConsumerFactory : IIngestionBatchConsumerFactory
{
    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly RabbitMqOptions _options;

    public RabbitMqIngestionBatchConsumerFactory(RabbitMqConnectionManager connectionManager, RabbitMqOptions options)
    {
        _connectionManager = connectionManager;
        _options = options;
    }

    public async ValueTask<IIngestionBatchConsumer> CreateAsync(CancellationToken ct)
    {
        // A worker must not share a RabbitMQ channel with another worker.
        var channel = await _connectionManager.CreateChannelAsync(options: null, ct);
        return new RabbitMqIngestionBatchConsumer(channel, _options.QueueName);
    }
}
