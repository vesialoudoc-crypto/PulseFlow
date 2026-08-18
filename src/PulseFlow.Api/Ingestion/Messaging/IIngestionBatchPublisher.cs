namespace PulseFlow.Api.Ingestion.Messaging;

public interface IIngestionBatchPublisher
{
    Task PublishAsync(
        ReadOnlyMemory<byte> rawBatch,
        CancellationToken cancellationToken);
}
