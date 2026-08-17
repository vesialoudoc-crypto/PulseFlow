using System.Text.Json;
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
    public async Task SaveChangesAsync_EventRecordIsValid_PersistsId()
    {
        // Arrange
        var expected = CreateEventRecord();

        // Act
        var actual = await SaveAndReadAsync(expected);

        // Assert
        Assert.Equal(expected.Id, actual.Id);
    }

    [Fact]
    public async Task SaveChangesAsync_EventRecordIsValid_PersistsType()
    {
        // Arrange
        var expected = CreateEventRecord();

        // Act
        var actual = await SaveAndReadAsync(expected);

        // Assert
        Assert.Equal("sensor.reading.recorded", actual.Type);
    }

    [Fact]
    public async Task SaveChangesAsync_EventRecordIsValid_PersistsSource()
    {
        // Arrange
        var expected = CreateEventRecord();

        // Act
        var actual = await SaveAndReadAsync(expected);

        // Assert
        Assert.Equal("urn:pulseflow:test:sensor-17", actual.Source);
    }

    [Fact]
    public async Task SaveChangesAsync_EventRecordHasNestedPayload_PersistsPayload()
    {
        // Arrange
        var expected = CreateEventRecord();
        var expectedPayload = JsonNode.Parse(expected.PayloadJson);

        // Act
        var actual = await SaveAndReadAsync(expected);
        var actualPayload = JsonNode.Parse(actual.PayloadJson);

        // Assert
        Assert.True(JsonNode.DeepEquals(expectedPayload, actualPayload));
    }

    [Fact]
    public async Task SaveChangesAsync_OccurredAtIsUtc_PersistsOccurredAt()
    {
        // Arrange
        var expected = CreateEventRecord();

        // Act
        var actual = await SaveAndReadAsync(expected);
        var actualOccurredAt = actual.OccurredAt.ToString("O");

        // Assert
        Assert.Equal("2026-08-16T09:10:11.0000000Z", actualOccurredAt);
    }

    [Fact]
    public async Task SaveChangesAsync_ReceivedAtIsUtc_PersistsReceivedAt()
    {
        // Arrange
        var expected = CreateEventRecord();

        // Act
        var actual = await SaveAndReadAsync(expected);
        var actualReceivedAt = actual.ReceivedAt.ToString("O");

        // Assert
        Assert.Equal("2026-08-16T09:10:12.0000000Z", actualReceivedAt);
    }

    private async Task<EventRecord> SaveAndReadAsync(EventRecord eventRecord)
    {
        await using (var writeContext = CreateDbContext())
        {
            await writeContext.Database.MigrateAsync();
            writeContext.EventRecords.Add(eventRecord);
            await writeContext.SaveChangesAsync();
        }

        await using var readContext = CreateDbContext();

        return await readContext.EventRecords
            .AsNoTracking()
            .SingleAsync(storedEvent => storedEvent.Id == eventRecord.Id);
    }

    private PulseFlowDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PulseFlowDbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        return new PulseFlowDbContext(options);
    }

    private static EventRecord CreateEventRecord()
    {
        return new EventRecord
        {
            Id = Guid.NewGuid(),
            Type = "sensor.reading.recorded",
            Source = "urn:pulseflow:test:sensor-17",
            OccurredAt = new DateTime(2026, 8, 16, 9, 10, 11, DateTimeKind.Utc),
            ReceivedAt = new DateTime(2026, 8, 16, 9, 10, 12, DateTimeKind.Utc),
            PayloadJson = JsonSerializer.Serialize(new
            {
                metadata = new
                {
                    attempt = 3,
                    labels = new[] { "priority", "external" },
                    enabled = true
                },
                measurements = new[]
                {
                    new { name = "temperature", value = 21.75m },
                    new { name = "humidity", value = 0.43m }
                }
            })
        };
    }
}
