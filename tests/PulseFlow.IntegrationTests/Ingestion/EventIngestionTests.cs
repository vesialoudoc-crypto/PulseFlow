using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PulseFlow.Api.Ingestion.Contracts;
using PulseFlow.Api.Ingestion.Persistence;
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
        Assert.Equal(1, result.Total);
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

        using var content = CreateEventContent(
            type,
            source,
            occurredAt,
            payload);

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

    [Fact]
    public async Task PostEvents_MixedRecords_ReturnsExpectedAccounting()
    {
        // Arrange
        var validA = CreateValidEventJson("accounting-a");
        const string malformed = "{ not-json";
        var invalid = JsonSerializer.Serialize(new { type = "invalid" });
        var validB = CreateValidEventJson("accounting-b");
        var validC = CreateValidEventJson("accounting-c");

        using var content = CreateNdjsonContent(
            validA,
            malformed,
            invalid,
            validB,
            validC);

        // Act
        using var response = await _client.PostAsync("/api/events", content);
        var result = await response.Content.ReadFromJsonAsync<IngestEventsResponse>();

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(result);
        Assert.Equal(5, result.Total);
        Assert.Equal(3, result.Accepted);
        Assert.Equal(2, result.Rejected);
    }

    [Fact]
    public async Task PostEvents_MixedRecords_PersistsValidEventsAcrossChunks()
    {
        // Arrange
        var occurredAtA = new DateTime(2026, 8, 17, 11, 0, 0, DateTimeKind.Utc);
        var occurredAtB = new DateTime(2026, 8, 17, 11, 1, 0, DateTimeKind.Utc);
        var occurredAtC = new DateTime(2026, 8, 17, 11, 2, 0, DateTimeKind.Utc);

        var validA = CreateEventJson(
            type: "chunk-a",
            source: "integration-test-a",
            occurredAtA,
            payload: new { value = "a" });

        const string malformed = "{ not-json";
        var invalid = JsonSerializer.Serialize(new { type = "invalid" });

        var validB = CreateEventJson(
            type: "chunk-b",
            source: "integration-test-b",
            occurredAtB,
            payload: new { value = "b" });

        var validC = CreateEventJson(
            type: "chunk-c",
            source: "integration-test-c",
            occurredAtC,
            payload: new { value = "c" });

        using var content = CreateNdjsonContent(
            validA,
            malformed,
            invalid,
            validB,
            validC);

        // Act
        using var response = await _client.PostAsync("/api/events", content);
        var storedEvents = await GetStoredEventsAsync();

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal(3, storedEvents.Count);

        Assert.Equal("chunk-a", storedEvents[0].Type);
        Assert.Equal("integration-test-a", storedEvents[0].Source);
        Assert.Equal(occurredAtA, storedEvents[0].OccurredAt);

        Assert.Equal("chunk-b", storedEvents[1].Type);
        Assert.Equal("integration-test-b", storedEvents[1].Source);
        Assert.Equal(occurredAtB, storedEvents[1].OccurredAt);

        Assert.Equal("chunk-c", storedEvents[2].Type);
        Assert.Equal("integration-test-c", storedEvents[2].Source);
        Assert.Equal(occurredAtC, storedEvents[2].OccurredAt);
    }

    [Fact]
    public async Task PostEvents_LaterChunkFails_KeepsEarlierCommittedChunk()
    {
        // Arrange
        const string firstType = "failure-a";
        const string secondType = "failure-b";
        const string failingFirstType = "failure-c";
        const string failingSecondType = "failure-d";

        using var failureFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IEventChunkStore>();
                services.AddScoped<IEventChunkStore>(serviceProvider =>
                    new FailOnSecondCallEventChunkStore(
                        serviceProvider.GetRequiredService<PulseFlowDbContext>()));
            });
        });
        using var failureClient = failureFactory.CreateClient();
        using var content = CreateNdjsonContent(
            CreateValidEventJson(firstType),
            CreateValidEventJson(secondType),
            CreateValidEventJson(failingFirstType),
            CreateValidEventJson(failingSecondType));

        // Act
        using var response = await failureClient.PostAsync("/api/events", content);
        var storedEvents = await GetStoredEventsAsync();

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(2, storedEvents.Count);
        Assert.Equal([firstType, secondType], storedEvents.Select(eventRecord => eventRecord.Type));
        Assert.DoesNotContain(storedEvents, eventRecord => eventRecord.Type == failingFirstType);
        Assert.DoesNotContain(storedEvents, eventRecord => eventRecord.Type == failingSecondType);
    }

    #region Test helpers

    private static StringContent CreateEventContent(
        string type,
        string source,
        DateTime occurredAt,
        object payload)
    {
        return CreateNdjsonContent(
            CreateEventJson(type, source, occurredAt, payload));
    }

    private static string CreateValidEventJson(string type)
    {
        return CreateEventJson(
            type,
            source: "integration-test",
            occurredAt: new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc),
            payload: new { value = 42 });
    }

    private static string CreateEventJson(
        string type,
        string source,
        DateTime occurredAt,
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

    private static StringContent CreateNdjsonContent(params string[] records)
    {
        return new StringContent(
            string.Join('\n', records),
            Encoding.UTF8,
            "application/x-ndjson");
    }

    private async Task<EventRecord> GetSingleStoredEventAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PulseFlowDbContext>();

        return await dbContext.EventRecords
            .AsNoTracking()
            .SingleAsync();
    }

    private async Task<List<EventRecord>> GetStoredEventsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PulseFlowDbContext>();

        return await dbContext.EventRecords
            .AsNoTracking()
            .OrderBy(eventRecord => eventRecord.Type)
            .ToListAsync();
    }

    private static void AssertJsonEquivalent(
        string expectedJson,
        string actualJson)
    {
        Assert.True(
            JsonNode.DeepEquals(
                JsonNode.Parse(expectedJson),
                JsonNode.Parse(actualJson)));
    }

    private sealed class FailOnSecondCallEventChunkStore : IEventChunkStore
    {
        private readonly EfCoreEventChunkStore _inner;
        private int _callCount;

        public FailOnSecondCallEventChunkStore(PulseFlowDbContext dbContext)
        {
            _inner = new EfCoreEventChunkStore(dbContext);
        }

        public Task StoreAsync(
            IReadOnlyCollection<EventEnvelope> events,
            CancellationToken cancellationToken = default)
        {
            _callCount++;

            if (_callCount == 2)
            {
                throw new InvalidOperationException(
                    "Deterministic integration-test persistence failure.");
            }

            return _inner.StoreAsync(events, cancellationToken);
        }
    }

    private sealed class IngestEventsResponse
    {
        public int Total { get; init; }

        public int Accepted { get; init; }

        public int Rejected { get; init; }
    }

    #endregion
}
