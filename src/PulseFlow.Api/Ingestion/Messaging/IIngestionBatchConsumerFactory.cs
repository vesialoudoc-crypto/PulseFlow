namespace PulseFlow.Api.Ingestion.Messaging;

public interface IIngestionBatchConsumerFactory
{
    ValueTask<IIngestionBatchConsumer> CreateAsync(
        CancellationToken cancellationToken);
}
