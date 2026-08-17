using PulseFlow.Api.Ingestion.Contracts;
using PulseFlow.Api.Ingestion.Persistence;

namespace PulseFlow.Api.Persistence.Events;

public sealed class EfCoreEventChunkStore : IEventChunkStore
{
    private readonly PulseFlowDbContext _dbContext;

    public EfCoreEventChunkStore(PulseFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task StoreAsync(
        IReadOnlyCollection<EventEnvelope> events,
        CancellationToken cancellationToken = default)
    {
        var eventRecords = events
            .Select(envelope => new EventRecord
            {
                Id = Guid.NewGuid(),
                Type = envelope.Type,
                Source = envelope.Source,
                OccurredAt = envelope.OccurredAt,
                ReceivedAt = DateTime.UtcNow,
                PayloadJson = envelope.Payload.GetRawText()
            })
            .ToArray();

        _dbContext.EventRecords.AddRange(eventRecords);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
