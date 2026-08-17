using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PulseFlow.Api.Persistence;
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
        const string ndjson =
            """{"type":"test","source":"integration-test","occurredAt":"2026-08-17T10:00:00Z","payload":{"value":42}}""";
        using var content = CreateNdjsonContent(ndjson);

        // Act
        using var response = await _client.PostAsync("/api/events", content);
        var result = await response.Content.ReadFromJsonAsync<IngestEventsResponse>();

        // Assert
        response.EnsureSuccessStatusCode();
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
        const string ndjson =
            """{"type":"test","source":"integration-test","occurredAt":"2026-08-17T10:00:00Z","payload":{"value":42}}""";
        using var content = CreateNdjsonContent(ndjson);

        // Act
        using var response = await _client.PostAsync("/api/events", content);

        // Assert
        response.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PulseFlowDbContext>();
        var storedEvent = await dbContext.EventRecords
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal("test", storedEvent.Type);
        Assert.Equal("integration-test", storedEvent.Source);
        Assert.Equal(
            new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc),
            storedEvent.OccurredAt);
        Assert.True(
            JsonNode.DeepEquals(
                JsonNode.Parse("""{"value":42}"""),
                JsonNode.Parse(storedEvent.PayloadJson)));
    }

    #region Test helpers

    private static StringContent CreateNdjsonContent(string ndjson)
    {
        return new StringContent(ndjson, Encoding.UTF8, "application/x-ndjson");
    }

    private sealed class IngestEventsResponse
    {
        public int Total { get; init; }

        public int Accepted { get; init; }

        public int Rejected { get; init; }
    }

    #endregion
}
