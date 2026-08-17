using PulseFlow.Api.Ingestion.Contracts;
using PulseFlow.Api.Ingestion.Ndjson;
using PulseFlow.Api.Ingestion.Persistence;
using PulseFlow.Api.Ingestion.Validation;

namespace PulseFlow.Api.Ingestion;

public sealed class IngestEventsHandler
{
    private readonly EventEnvelopeValidator _validator;
    private readonly IEventChunkStore _eventChunkStore;
    private readonly int _chunkCapacity;

    public IngestEventsHandler(
        EventEnvelopeValidator validator,
        IEventChunkStore eventChunkStore,
        int chunkCapacity)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(eventChunkStore);

        if(chunkCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkCapacity),
                chunkCapacity,
                "Chunk capacity must be greater than zero.");
        }

        _validator = validator;
        _eventChunkStore = eventChunkStore;
        _chunkCapacity = chunkCapacity;
    }

    public async Task<IngestEventsResult> HandleAsync(
        IAsyncEnumerable<NdjsonRecordResult> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        var total = 0;
        var accepted = 0;
        var currentChunk = new List<EventEnvelope>(_chunkCapacity);

        await foreach(var record in records.WithCancellation(cancellationToken))
        {
            total++;

            // Malformed NDJSON is rejected before contract validation.
            if(record.IsMalformed)
            {
                continue;
            }

            var validationResult = _validator.Validate(record.ParsedJson!.Value);

            // Valid JSON can still violate the event contract.
            if(!validationResult.IsValid)
            {
                continue;
            }

            currentChunk.Add(validationResult.Envelope!);

            // Persist full chunks as soon as they are ready.
            if(currentChunk.Count == _chunkCapacity)
            {
                await _eventChunkStore.StoreAsync(currentChunk, cancellationToken);
                accepted += currentChunk.Count;
                currentChunk = new List<EventEnvelope>(_chunkCapacity);
            }
        }

        // Flush the final incomplete chunk.
        if(currentChunk.Count > 0)
        {
            await _eventChunkStore.StoreAsync(currentChunk, cancellationToken);
            accepted += currentChunk.Count;
        }

        return new IngestEventsResult(total, accepted);
    }
}