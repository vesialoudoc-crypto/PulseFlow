using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using PulseFlow.Api.Persistence;
using PulseFlow.Api.Persistence.Events;
using PulseFlow.IntegrationTests.Infrastructure;

namespace PulseFlow.IntegrationTests.Persistence;

public sealed class PulseFlowDbContextPersistenceTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PulseFlowDbContextPersistenceTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Event_record_round_trips_through_migrated_postgresql_database()
    {
        const string payloadJson = """
            {
              "metadata": {
                "attempt": 3,
                "labels": ["priority", "external"],
                "enabled": true
              },
              "measurements": [
                { "name": "temperature", "value": 21.75 },
                { "name": "humidity", "value": 0.43 }
              ]
            }
            """;
        var expected = new EventRecord
        {
            Id = Guid.Parse("bbdc5c2d-f84d-46e8-8cb4-3272d7b85dc4"),
            Type = "sensor.reading.recorded",
            Source = "urn:pulseflow:test:sensor-17",
            OccurredAt = new DateTime(2026, 8, 16, 9, 10, 11, 123, DateTimeKind.Utc)
                .AddTicks(4_560),
            ReceivedAt = new DateTime(2026, 8, 16, 9, 10, 12, 987, DateTimeKind.Utc)
                .AddTicks(6_540),
            PayloadJson = payloadJson
        };

        await using (var writeContext = CreateDbContext())
        {
            await writeContext.Database.MigrateAsync();
            writeContext.EventRecords.Add(expected);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = CreateDbContext();
        var actual = await readContext.EventRecords
            .AsNoTracking()
            .SingleAsync(eventRecord => eventRecord.Id == expected.Id);

        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.Source, actual.Source);
        AssertJsonSemanticallyEqual(expected.PayloadJson, actual.PayloadJson);
        AssertUtcInstant(expected.OccurredAt, actual.OccurredAt);
        AssertUtcInstant(expected.ReceivedAt, actual.ReceivedAt);
    }

    private PulseFlowDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PulseFlowDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        return new PulseFlowDbContext(options);
    }

    private static void AssertJsonSemanticallyEqual(string expectedJson, string actualJson)
    {
        var expected = JsonNode.Parse(expectedJson);
        var actual = JsonNode.Parse(actualJson);

        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"Expected JSON '{expectedJson}' to be semantically equal to '{actualJson}'.");
    }

    private static void AssertUtcInstant(DateTime expected, DateTime actual)
    {
        Assert.Equal(DateTimeKind.Utc, actual.Kind);
        Assert.Equal(expected, actual);
    }
}
