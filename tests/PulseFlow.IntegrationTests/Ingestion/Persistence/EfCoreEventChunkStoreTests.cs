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
            })),
            CreateValidEnvelope(CreateRecordJson(
            type: "device.status.changed",
            source: "urn:pulseflow:test:device-42",
            occurredAt: "2026-08-17T10:12:13Z",
            payload: new
            {
                online = true, metadata = new { attempt = 3, reason = null as object }
            }))
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
        var storedRecords = await readContext.EventRecords
            .AsNoTracking()
            .OrderBy(eventRecord => eventRecord.Type)
            .ToArrayAsync();
        var expectedContractValues = envelopes
            .OrderBy(envelope => envelope.Type)
            .Select(envelope => (envelope.Type, envelope.Source, envelope.OccurredAt))
            .ToArray();
        var actualContractValues = storedRecords
            .Select(eventRecord => (eventRecord.Type, eventRecord.Source, eventRecord.OccurredAt))
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
        object payload)
    {
        return JsonSerializer.Serialize(new
        {
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
            ?? throw new InvalidOperationException("The test input must be a valid Event Contract v1 envelope.");
    }

    #endregion
}
