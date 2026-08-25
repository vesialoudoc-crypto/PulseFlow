namespace PulseFlow.Api.Ingestion.Http;

public interface IIngestionBatchBodyReader
{
    Task<IngestionBatchBodyReadResult> ReadAsync(Stream body, long? contentLength, CancellationToken ct);
}
