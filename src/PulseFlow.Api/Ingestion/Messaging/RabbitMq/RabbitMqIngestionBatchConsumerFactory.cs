using System.Collections.Concurrent;

namespace PulseFlow.Api.Ingestion.Messaging.RabbitMq;

internal sealed class RabbitMqIngestionBatchConsumerFactory : IIngestionBatchConsumerFactory
{
    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly RabbitMqOptions _options;
    private readonly ConcurrentBag<RabbitMqIngestionBatchConsumer> _consumers = [];

    public RabbitMqIngestionBatchConsumerFactory(
        RabbitMqConnectionManager connectionManager,
        RabbitMqOptions options)
    {
        _connectionManager = connectionManager;
        _options = options;
    }

    internal IReadOnlyCollection<RabbitMqIngestionBatchConsumer> Consumers => _consumers;

    public async ValueTask<IIngestionBatchConsumer> CreateAsync(
        CancellationToken cancellationToken)
    {
        // A worker must not share a RabbitMQ channel with another worker.
        var channel = await _connectionManager.CreateChannelAsync(
            options: null,
            cancellationToken);
        var consumer = new RabbitMqIngestionBatchConsumer(channel, _options.QueueName);
        _consumers.Add(consumer);

        return consumer;
    }
}
