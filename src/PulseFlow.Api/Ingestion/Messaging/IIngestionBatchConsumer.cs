namespace PulseFlow.Api.Ingestion.Messaging;

public interface IIngestionBatchConsumer : IAsyncDisposable
{
    IAsyncEnumerable<IngestionBatchDelivery> ReadAllAsync(
        CancellationToken cancellationToken);
}
