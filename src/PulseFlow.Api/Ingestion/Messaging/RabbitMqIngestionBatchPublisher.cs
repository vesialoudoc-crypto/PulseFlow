using RabbitMQ.Client;

namespace PulseFlow.Api.Ingestion.Messaging;

public sealed class RabbitMqIngestionBatchPublisher : IIngestionBatchPublisher
{
    private readonly IChannel _channel;
    private readonly RabbitMqOptions _options;
    private readonly SemaphoreSlim _publishGate = new(1, 1);

    public RabbitMqIngestionBatchPublisher(
        IChannel channel,
        RabbitMqOptions options)
    {
        _channel = channel;
        _options = options;
    }

    public async Task PublishAsync(
        ReadOnlyMemory<byte> rawBatch,
        CancellationToken cancellationToken)
    {
        await _publishGate.WaitAsync(cancellationToken);

        try
        {
            await _channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: _options.QueueName,
                mandatory: true,
                basicProperties: new BasicProperties
                {
                    ContentType = "application/x-ndjson",
                    DeliveryMode = DeliveryModes.Persistent
                },
                body: rawBatch,
                cancellationToken: cancellationToken);
        }
        finally
        {
            _publishGate.Release();
        }
    }
}
