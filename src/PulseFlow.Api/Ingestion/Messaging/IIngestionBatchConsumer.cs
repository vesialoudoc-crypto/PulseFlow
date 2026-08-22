namespace PulseFlow.Api.Ingestion.Messaging;

public interface IIngestionBatchConsumer : IAsyncDisposable
{
    Task ConsumeAsync(
        IngestionBatchHandler handler,
        CancellationToken cancellationToken);
}
