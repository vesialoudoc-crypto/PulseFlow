using Microsoft.EntityFrameworkCore;
using PulseFlow.Api.Ingestion.Contracts;
using PulseFlow.Api.Ingestion.Validation;
using PulseFlow.Api.Persistence;
using PulseFlow.Api.Persistence.Events;
using PulseFlow.IntegrationTests.Infrastructure;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PulseFlow.IntegrationTests.Ingestion.Persistence;

public sealed class EfCoreEventChunkStoreTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public EfCoreEventChunkStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task StoreAsync_ValidEnvelopeChunk_PersistsMappedRecords()
    {
        // Arrange
        var envelopes = new[]
        {
            CreateValidEnvelope(CreateRecordJson(
            type: "sensor.reading.recorded",
            source: "urn:pulseflow:test:sensor-17",
            occurredAt: "2026-08-17T10:11:12Z",
            payload: new
            {
                name = "temperature", value =  21.75, labels = new []{ "priority", "external" }
            },
            eventId: Guid.Parse("1bf6ad26-dc07-415a-a9a4-f1dda05e6fb3"))),
            CreateValidEnvelope(CreateRecordJson(
            type: "device.status.changed",
            source: "urn:pulseflow:test:device-42",
            occurredAt: "2026-08-17T10:12:13Z",
            payload: new
            {
                online = true, metadata = new { attempt = 3, reason = null as object }
            },
            eventId: Guid.Parse("39a1d1b5-9e75-49e5-93e4-3f45c1e78a01")))
        };
        await using var writeContext = CreateDbContext();
        await writeContext.Database.MigrateAsync();
        var store = new EfCoreEventChunkStore(writeContext);
        var earliestExpectedReceivedAt = DateTime.UtcNow.AddMilliseconds(-1);

        // Act
        await store.StoreAsync(envelopes);
        var latestExpectedReceivedAt = DateTime.UtcNow.AddMilliseconds(1);

        // Assert
        await using var readContext = CreateDbContext();
        var expectedEventIds = envelopes.Select(envelope => envelope.EventId).ToArray();
        var storedRecords = await readContext.EventRecords
            .AsNoTracking()
            .Where(eventRecord => expectedEventIds.Contains(eventRecord.EventId))
            .OrderBy(eventRecord => eventRecord.Type)
            .ToArrayAsync();
        var expectedContractValues = envelopes
            .OrderBy(envelope => envelope.Type)
            .Select(envelope => (envelope.EventId, envelope.Type, envelope.Source, envelope.OccurredAt))
            .ToArray();
        var actualContractValues = storedRecords
            .Select(eventRecord => (
                eventRecord.EventId,
                eventRecord.Type,
                eventRecord.Source,
                eventRecord.OccurredAt))
            .ToArray();
        var expectedPayloads = envelopes.ToDictionary(
            envelope => envelope.Type,
            envelope => JsonNode.Parse(envelope.Payload.GetRawText()));

        Assert.Equal(2, storedRecords.Length);
        Assert.All(storedRecords, eventRecord => Assert.NotEqual(Guid.Empty, eventRecord.Id));
        Assert.Equal(2, storedRecords.Select(eventRecord => eventRecord.Id).Distinct().Count());
        Assert.Equal(expectedContractValues, actualContractValues);
        Assert.All(
            storedRecords,
            eventRecord => Assert.True(
                JsonNode.DeepEquals(
                    expectedPayloads[eventRecord.Type],
                    JsonNode.Parse(eventRecord.PayloadJson))));
        Assert.All(storedRecords, eventRecord =>
        {
            Assert.Equal(DateTimeKind.Utc, eventRecord.ReceivedAt.Kind);
            Assert.InRange(
                eventRecord.ReceivedAt,
                earliestExpectedReceivedAt,
                latestExpectedReceivedAt);
        });
    }

    [Fact]
    public async Task StoreAsync_SameSourceAndEventIdTwice_PersistsOneRecord()
    {
        // Arrange
        const string Source = "urn:pulseflow:test:duplicate-source";
        var eventId = Guid.Parse("6d72b7e3-6a2e-4ab7-bf61-2d7667d15679");
        var envelope = CreateValidEnvelope(CreateRecordJson(
            "duplicate.event",
            Source,
            "2026-08-20T10:00:00Z",
            new { },
            eventId));
        await using var writeContext = CreateDbContext();
        await writeContext.Database.MigrateAsync();
        var store = new EfCoreEventChunkStore(writeContext);

        // Act
        await store.StoreAsync(new[] { envelope });
        await store.StoreAsync(new[] { envelope });

        // Assert
        await using var readContext = CreateDbContext();
        var count = await readContext.EventRecords.CountAsync(eventRecord =>
            eventRecord.Source == Source && eventRecord.EventId == eventId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task StoreAsync_SameEventIdWithDifferentSources_PersistsTwoRecords()
    {
        // Arrange
        var eventId = Guid.Parse("5ed6f808-7188-49b4-b5dd-46040ab014a6");
        var firstEnvelope = CreateValidEnvelope(CreateRecordJson(
            "source.one.event",
            "urn:pulseflow:test:source-one",
            "2026-08-20T10:00:00Z",
            new { },
            eventId));
        var secondEnvelope = CreateValidEnvelope(CreateRecordJson(
            "source.two.event",
            "urn:pulseflow:test:source-two",
            "2026-08-20T10:00:00Z",
            new { },
            eventId));
        await using var writeContext = CreateDbContext();
        await writeContext.Database.MigrateAsync();
        var store = new EfCoreEventChunkStore(writeContext);

        // Act
        await store.StoreAsync(new[] { firstEnvelope, secondEnvelope });

        // Assert
        await using var readContext = CreateDbContext();
        var count = await readContext.EventRecords.CountAsync(eventRecord =>
            eventRecord.EventId == eventId &&
            (eventRecord.Source == "urn:pulseflow:test:source-one" ||
             eventRecord.Source == "urn:pulseflow:test:source-two"));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task StoreAsync_ConcurrentDuplicateOperations_PersistsOneRecordWithoutFailure()
    {
        // Arrange
        const string Source = "urn:pulseflow:test:concurrent-duplicate";
        var eventId = Guid.Parse("8e59c29b-f5ce-4e48-847e-ccd1b0cc1e1e");
        var envelope = CreateValidEnvelope(CreateRecordJson(
            "concurrent.event",
            Source,
            "2026-08-20T10:00:00Z",
            new { },
            eventId));
        await using (var migrationContext = CreateDbContext())
        {
            await migrationContext.Database.MigrateAsync();
        }

        await using var firstContext = CreateDbContext();
        await using var secondContext = CreateDbContext();
        var firstStore = new EfCoreEventChunkStore(firstContext);
        var secondStore = new EfCoreEventChunkStore(secondContext);

        // Act
        await Task.WhenAll(
            firstStore.StoreAsync(new[] { envelope }),
            secondStore.StoreAsync(new[] { envelope }));

        // Assert
        await using var readContext = CreateDbContext();
        var count = await readContext.EventRecords.CountAsync(eventRecord =>
            eventRecord.Source == Source && eventRecord.EventId == eventId);
        Assert.Equal(1, count);
    }

    #region Test helpers

    private PulseFlowDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PulseFlowDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        return new PulseFlowDbContext(options);
    }

    private static string CreateRecordJson(
        string type,
        string source,
        string occurredAt,
        object payload,
        Guid? eventId = null)
    {
        return JsonSerializer.Serialize(new
        {
            eventId = eventId ?? Guid.NewGuid(),
            type,
            source,
            occurredAt,
            payload
        });
    }

    private static EventEnvelope CreateValidEnvelope(string json)
    {
        using var document = JsonDocument.Parse(json);
        var validationResult = new EventEnvelopeValidator().Validate(document.RootElement);

        return validationResult.Envelope
            ?? throw new InvalidOperationException("The test input must be a valid Event Contract v2 envelope.");
    }

    #endregion
}
