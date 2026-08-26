using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PulseFlow.Api.Ingestion;
using PulseFlow.Api.Ingestion.Contracts;
using PulseFlow.Api.Ingestion.Ndjson;
using PulseFlow.Api.Ingestion.Persistence;
using PulseFlow.Api.Ingestion.Validation;
using PulseFlow.Api.Persistence;
using PulseFlow.Api.Persistence.Events;
using PulseFlow.IntegrationTests.Infrastructure;

namespace PulseFlow.IntegrationTests.Ingestion;

public sealed class IngestEventsHandlerIdempotencyTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public IngestEventsHandlerIdempotencyTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_LaterChunkFailsThenCompleteBatchReplays_PersistsEachLogicalEventOnce()
    {
        // Arrange
        const string Source = "urn:pulseflow:test:partial-replay";
        var eventIds = new[]
        {
            Guid.Parse("9e3a4658-185a-4d5d-bd05-442ef073aece"),
            Guid.Parse("8ea8f963-e951-4e9f-bf1e-71d78d267cad"),
            Guid.Parse("a105484a-c801-4309-8171-442a16ad91d7"),
            Guid.Parse("e629a6f6-f1f0-41e3-af4c-70443839c27f")
        };
        var batch = string.Join(
            '\n',
            eventIds.Select((eventId, index) => CreateRecordJson(
                eventId,
                $"partial-replay-{index + 1}",
                Source)).Append(string.Empty));

        await using (var migrationContext = CreateDbContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        await using var firstContext = CreateDbContext();
        var failingHandler = CreateHandler(
            new FailOnStoreCallEventChunkStore(
                new EfCoreEventChunkStore(firstContext, Options.Create(new PostgreSqlOptions())),
                failingCallNumber: 2));

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => failingHandler.HandleAsync(ReadRecordsAsync(batch)));

        await using (var replayContext = CreateDbContext())
        {
            var replayHandler = CreateHandler(
                new EfCoreEventChunkStore(replayContext, Options.Create(new PostgreSqlOptions())));
            await replayHandler.HandleAsync(ReadRecordsAsync(batch));
        }

        // Assert
        await using var readContext = CreateDbContext();
        var storedEventIds = await readContext.EventRecords
            .AsNoTracking()
            .Where(eventRecord => eventRecord.Source == Source)
            .OrderBy(eventRecord => eventRecord.EventId)
            .Select(eventRecord => eventRecord.EventId)
            .ToArrayAsync();
        Assert.Equal(4, storedEventIds.Length);
        Assert.Equal(eventIds.Order(), storedEventIds.Order());
    }

    #region Test helpers

    private PulseFlowDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PulseFlowDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        return new PulseFlowDbContext(options);
    }

    private static IngestEventsHandler CreateHandler(IEventChunkStore eventChunkStore)
    {
        return new IngestEventsHandler(
            new EventEnvelopeValidator(),
            eventChunkStore,
            chunkCapacity: 2);
    }

    private static async IAsyncEnumerable<NdjsonRecordResult> ReadRecordsAsync(string batch)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(batch));
        var reader = new NdjsonRecordReader();

        await foreach (var record in reader.ReadAsync(stream))
        {
            yield return record;
        }
    }

    private static string CreateRecordJson(Guid eventId, string type, string source)
    {
        return JsonSerializer.Serialize(new
        {
            eventId,
            type,
            source,
            occurredAt = "2026-08-20T10:00:00Z",
            payload = new { type }
        });
    }

    private sealed class FailOnStoreCallEventChunkStore : IEventChunkStore
    {
        private readonly IEventChunkStore _inner;
        private readonly int _failingCallNumber;
        private int _callCount;

        public FailOnStoreCallEventChunkStore(IEventChunkStore inner, int failingCallNumber)
        {
            _inner = inner;
            _failingCallNumber = failingCallNumber;
        }

        public Task StoreAsync(
            IReadOnlyCollection<EventEnvelope> events,
            CancellationToken cancellationToken = default)
        {
            _callCount++;

            if (_callCount == _failingCallNumber)
            {
                throw new InvalidOperationException("Expected persistence failure.");
            }

            return _inner.StoreAsync(events, cancellationToken);
        }
    }

    #endregion
}
