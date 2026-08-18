namespace PulseFlow.Api.Ingestion.Messaging;

public interface IRabbitMqPublisherChannelProvider
{
    Task PublishAsync(
        ReadOnlyMemory<byte> rawBatch,
        CancellationToken cancellationToken);
}
