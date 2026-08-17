using PulseFlow.Api.Ingestion.Contracts;

namespace PulseFlow.Api.Ingestion.Persistence;

public interface IEventChunkStore
{
    Task StoreAsync(
        IReadOnlyCollection<EventEnvelope> events,
        CancellationToken cancellationToken = default);
}
