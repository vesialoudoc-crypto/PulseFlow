using System.Text;
using System.Text.Json;
using PulseFlow.Api.Ingestion;
using PulseFlow.Api.Ingestion.Contracts;
using PulseFlow.Api.Ingestion.Ndjson;
using PulseFlow.Api.Ingestion.Persistence;
using PulseFlow.Api.Ingestion.Validation;

namespace PulseFlow.UnitTests.Ingestion;

public sealed class IngestEventsHandlerTests
{
    [Fact]
    public async Task HandleAsync_MalformedAndContractInvalidRecords_RejectsThemWithoutStoringThem()
    {
        // Arrange
        var input = string.Join(
            '\n',
            "not-json",
            CreateRecordJson("invalid", payload: new[] { 1, 2, 3 }),
            CreateRecordJson("valid"),
            string.Empty);
        var store = new RecordingEventChunkStore();
        var handler = CreateHandler(store, chunkCapacity: 3);

        // Act
        var result = await handler.HandleAsync(ReadRecordsAsync(input));

        // Assert
        Assert.Equal(3, result.Total);
        Assert.Equal(1, result.Accepted);
        Assert.Equal(2, result.Rejected);

        var storedChunk = Assert.Single(store.SuccessfulChunks);
        var storedEvent = Assert.Single(storedChunk);
        Assert.Equal("valid", storedEvent.Type);
    }

    [Theory]
    [InlineData(2, new[] { 2, 2, 1 })]
    [InlineData(3, new[] { 3, 2 })]
    public async Task HandleAsync_ValidRecords_PersistsOrderedFullAndFinalPartialChunks(
        int chunkCapacity,
        int[] expectedChunkSizes)
    {
        // Arrange
        var input = string.Join(
            '\n',
            Enumerable.Range(1, 5).Select(index => CreateRecordJson($"event-{index}"))) + '\n';
        var store = new RecordingEventChunkStore();
        var handler = CreateHandler(store, chunkCapacity);
        var expectedTypes = Enumerable.Range(1, 5)
            .Select(index => $"event-{index}")
            .ToArray();

        // Act
        var result = await handler.HandleAsync(ReadRecordsAsync(input));

        // Assert

        Assert.Equal(5, result.Total);
        Assert.Equal(5, result.Accepted);
        Assert.Equal(0, result.Rejected);
        Assert.Equal(expectedChunkSizes, store.SuccessfulChunks.Select(chunk => chunk.Count));
        Assert.Equal(
            expectedTypes,
            store.SuccessfulChunks.SelectMany(chunk => chunk).Select(@event => @event.Type));
    }

    [Fact]
    public async Task HandleAsync_LaterStoreCallThrows_PropagatesAndStopsAfterEarlierSuccessfulChunk()
    {
        // Arrange
        var input = string.Join(
            '\n',
            Enumerable.Range(1, 5).Select(index => CreateRecordJson($"event-{index}"))) + '\n';
        var expectedException = new InvalidOperationException("Persistence failed.");
        var store = new RecordingEventChunkStore(failingCallNumber: 2, expectedException);
        var handler = CreateHandler(store, chunkCapacity: 2);

        // Act
        var actualException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(ReadRecordsAsync(input)));

        // Assert
        Assert.Same(expectedException, actualException);
        Assert.Equal(2, store.AttemptedChunks.Count);

        var successfulChunk = Assert.Single(store.SuccessfulChunks);
        Assert.Equal(new[] { "event-1", "event-2" }, successfulChunk.Select(@event => @event.Type));
        Assert.Equal(
            new[] { "event-3", "event-4" },
            store.AttemptedChunks[1].Select(@event => @event.Type));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveChunkCapacity_ThrowsArgumentOutOfRangeException(
        int chunkCapacity)
    {
        // Arrange
        var validator = new EventEnvelopeValidator();
        var store = new RecordingEventChunkStore();

        // Act
        var exception = Record.Exception(
            () => new IngestEventsHandler(validator, store, chunkCapacity));

        // Assert
        var argumentException = Assert.IsType<ArgumentOutOfRangeException>(exception);
        Assert.Equal("chunkCapacity", argumentException.ParamName);
    }

    #region Test helpers

    private static IngestEventsHandler CreateHandler(
        IEventChunkStore store,
        int chunkCapacity)
    {
        return new IngestEventsHandler(
            new EventEnvelopeValidator(),
            store,
            chunkCapacity);
    }

    private static async IAsyncEnumerable<NdjsonRecordResult> ReadRecordsAsync(string input)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(input));
        var reader = new NdjsonRecordReader();

        await foreach (var record in reader.ReadAsync(stream))
        {
            yield return record;
        }
    }

    private static string CreateRecordJson(string type, object? payload = null)
    {
        return JsonSerializer.Serialize(new
        {
            type,
            source = "test-source",
            occurredAt = "2026-08-17T10:00:00Z",
            payload = payload ?? new { sequence = type }
        });
    }

    private sealed class RecordingEventChunkStore : IEventChunkStore
    {
        private readonly int? _failingCallNumber;
        private readonly Exception? _exception;

        public RecordingEventChunkStore(
            int? failingCallNumber = null,
            Exception? exception = null)
        {
            _failingCallNumber = failingCallNumber;
            _exception = exception;
        }

        public List<IReadOnlyList<EventEnvelope>> AttemptedChunks { get; } = [];

        public List<IReadOnlyList<EventEnvelope>> SuccessfulChunks { get; } = [];

        public Task StoreAsync(
            IReadOnlyCollection<EventEnvelope> events,
            CancellationToken cancellationToken = default)
        {
            var retainedChunk = events.ToArray();
            AttemptedChunks.Add(retainedChunk);

            if (AttemptedChunks.Count == _failingCallNumber)
            {
                throw _exception!;
            }

            SuccessfulChunks.Add(retainedChunk);
            return Task.CompletedTask;
        }
    }

    #endregion
}
