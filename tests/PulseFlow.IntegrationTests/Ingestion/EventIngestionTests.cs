using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PulseFlow.Api.Persistence;
using PulseFlow.Api.Persistence.Events;
using PulseFlow.IntegrationTests.Infrastructure;

namespace PulseFlow.IntegrationTests.Ingestion;

public sealed class EventIngestionTests :
    IClassFixture<PostgreSqlFixture>,
    IAsyncLifetime
{
    private readonly PostgreSqlFixture _postgres;
    private PulseFlowWebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public EventIngestionTests(PostgreSqlFixture postgres)
    {
        _postgres = postgres;
    }

    public async Task InitializeAsync()
    {
        _factory = new PulseFlowWebApplicationFactory<Program>(
            _postgres.ConnectionString);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PulseFlowDbContext>();

        await dbContext.Database.MigrateAsync();
        await dbContext.EventRecords.ExecuteDeleteAsync();

        _client = _factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();

        return Task.CompletedTask;
    }

    [Fact]
    public async Task PostEvents_ValidEvent_ReturnsAcceptedResult()
    {
        // Arrange
        var occurredAt = new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc);
        var payload = new { value = 42 };
        using var content = CreateEventContent(
            type: "test",
            source: "integration-test",
            occurredAt,
            payload);

        // Act
        using var response = await _client.PostAsync("/api/events", content);
        var result = await response.Content.ReadFromJsonAsync<IngestEventsResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(1, result!.Total);
        Assert.Equal(1, result.Accepted);
        Assert.Equal(0, result.Rejected);
    }

    [Fact]
    public async Task PostEvents_ValidEvent_PersistsEvent()
    {
        // Arrange
        const string type = "test";
        const string source = "integration-test";
        var occurredAt = new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc);
        var payload = new { value = 42 };
        using var content = CreateEventContent(type, source, occurredAt, payload);

        // Act
        using var response = await _client.PostAsync("/api/events", content);
        var storedEvent = await GetSingleStoredEventAsync();

        // Assert
        response.EnsureSuccessStatusCode();

        Assert.Equal(type, storedEvent.Type);
        Assert.Equal(source, storedEvent.Source);
        Assert.Equal(occurredAt, storedEvent.OccurredAt);
        AssertJsonEquivalent(
            JsonSerializer.Serialize(payload),
            storedEvent.PayloadJson);
    }

    #region Test helpers

    private static StringContent CreateEventContent(
        string type,
        string source,
        DateTime occurredAt,
        object payload)
    {
        var ndjson = JsonSerializer.Serialize(new
        {
            type,
            source,
            occurredAt,
            payload
        });

        return new StringContent(ndjson, Encoding.UTF8, "application/x-ndjson");
    }

    private async Task<EventRecord> GetSingleStoredEventAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PulseFlowDbContext>();

        return await dbContext.EventRecords
            .AsNoTracking()
            .SingleAsync();
    }

    private static void AssertJsonEquivalent(string expectedJson, string actualJson)
    {
        Assert.True(
            JsonNode.DeepEquals(
                JsonNode.Parse(expectedJson),
                JsonNode.Parse(actualJson)));
    }

    private sealed class IngestEventsResponse
    {
        public int Total { get; init; }

        public int Accepted { get; init; }

        public int Rejected { get; init; }
    }

    #endregion
}
