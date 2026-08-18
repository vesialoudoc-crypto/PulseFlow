namespace PulseFlow.Api.Ingestion.Messaging;

public sealed class RabbitMqIngestionBatchPublisher : IIngestionBatchPublisher
{
    private readonly IRabbitMqPublisherChannelProvider _channelProvider;

    public RabbitMqIngestionBatchPublisher(
        IRabbitMqPublisherChannelProvider channelProvider)
    {
        _channelProvider = channelProvider;
    }

    public Task PublishAsync(
        ReadOnlyMemory<byte> rawBatch,
        CancellationToken cancellationToken)
    {
        return _channelProvider.PublishAsync(
            rawBatch,
            cancellationToken);
    }
}
