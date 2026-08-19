using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PulseFlow.Api.Ingestion;
using PulseFlow.Api.Ingestion.Contracts;
using PulseFlow.Api.Ingestion.Messaging;
using PulseFlow.Api.Ingestion.Ndjson;
using PulseFlow.Api.Ingestion.Persistence;
using PulseFlow.Api.Ingestion.Validation;

namespace PulseFlow.UnitTests.Ingestion.Messaging;

public sealed class EventParserConsumerTests
{
    [Fact]
    public async Task EventParserConsumer_ValidRawBatch_PassesRecordsThroughExistingParsingAndPersistencePath()
    {
        // Arrange
        var store = new RecordingEventChunkStore();
        await using var testContext = CreateTestContext(store);
        var rawBatch = Encoding.UTF8.GetBytes(
            CreateRecordJson("first") + "\n" + CreateRecordJson("second") + "\n");

        // Act
        await testContext.DeliverAsync(rawBatch);

        // Assert
        var persistedTypes = Assert.Single(store.SuccessfulChunks)
            .Select(@event => @event.Type)
            .ToArray();
        Assert.Equal(new[] { "first", "second" }, persistedTypes);
    }

    [Fact]
    public async Task EventParserConsumer_MalformedAndContractInvalidRecords_UsesExistingRejectionBehavior()
    {
        // Arrange
        var store = new RecordingEventChunkStore();
        await using var testContext = CreateTestContext(store);
        var rawBatch = string.Join(
            '\n',
            "not-json",
            CreateRecordJson("invalid", payload: new[] { 1, 2, 3 }),
            CreateRecordJson("valid"),
            string.Empty);
        // Act
        await testContext.DeliverAsync(Encoding.UTF8.GetBytes(rawBatch));

        // Assert
        var persistedEvent = Assert.Single(Assert.Single(store.SuccessfulChunks));
        Assert.Equal("valid", persistedEvent.Type);
    }

    [Fact]
    public async Task EventParserConsumer_ProcessingSucceeds_AcknowledgesDelivery()
    {
        // Arrange
        var store = new RecordingEventChunkStore();
        await using var testContext = CreateTestContext(store);
        // Act
        await testContext.DeliverAsync(Encoding.UTF8.GetBytes(CreateRecordJson("valid") + "\n"));

        // Assert
        Assert.Equal(1, testContext.Consumer.AcknowledgementCount);
    }

    [Fact]
    public async Task EventParserConsumer_ProcessingFails_DoesNotAcknowledgeDelivery()
    {
        // Arrange
        var store = new RecordingEventChunkStore(new InvalidOperationException("Persistence failed."));
        await using var testContext = CreateTestContext(store);
        // Act
        await testContext.SendAsync(Encoding.UTF8.GetBytes(CreateRecordJson("valid") + "\n"));
        await store.StoreAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.Equal(0, testContext.Consumer.AcknowledgementCount);
    }

    #region Test helpers

    private static ConsumerTestContext CreateTestContext(RecordingEventChunkStore store)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new EventEnvelopeValidator());
        services.AddSingleton<NdjsonRecordReader>();
        services.AddScoped<IEventChunkStore>(_ => store);
        services.AddScoped<IngestEventsHandler>(serviceProvider => new IngestEventsHandler(
            serviceProvider.GetRequiredService<EventEnvelopeValidator>(),
            serviceProvider.GetRequiredService<IEventChunkStore>(),
            chunkCapacity: 10));

        return new ConsumerTestContext(services.BuildServiceProvider(), new TestIngestionBatchConsumer());
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

    private sealed class ConsumerTestContext : IAsyncDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly CancellationTokenSource _stoppingSource = new();
        private readonly EventParserConsumer _eventParserConsumer;

        public ConsumerTestContext(
            ServiceProvider serviceProvider,
            TestIngestionBatchConsumer consumer)
        {
            _serviceProvider = serviceProvider;
            Consumer = consumer;
            _eventParserConsumer = new EventParserConsumer(
                new TestIngestionBatchConsumerFactory(Consumer),
                _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                _serviceProvider.GetRequiredService<NdjsonRecordReader>(),
                NullLogger<EventParserConsumer>.Instance,
                consumerCount: 1);
        }

        public TestIngestionBatchConsumer Consumer { get; }

        public async Task DeliverAsync(ReadOnlyMemory<byte> body)
        {
            await _eventParserConsumer.StartAsync(_stoppingSource.Token);
            await Consumer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Consumer.DeliverAsync(body, _stoppingSource.Token);
        }

        public async Task SendAsync(ReadOnlyMemory<byte> body)
        {
            await _eventParserConsumer.StartAsync(_stoppingSource.Token);
            await Consumer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Consumer.SendAsync(body, _stoppingSource.Token);
        }

        public async ValueTask DisposeAsync()
        {
            _stoppingSource.Cancel();
            await _eventParserConsumer.StopAsync(CancellationToken.None);
            _stoppingSource.Dispose();
            await _serviceProvider.DisposeAsync();
        }
    }

    private sealed class TestIngestionBatchConsumerFactory : IIngestionBatchConsumerFactory
    {
        private readonly TestIngestionBatchConsumer _consumer;

        public TestIngestionBatchConsumerFactory(TestIngestionBatchConsumer consumer)
        {
            _consumer = consumer;
        }

        public ValueTask<IIngestionBatchConsumer> CreateAsync(
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<IIngestionBatchConsumer>(_consumer);
        }
    }

    private sealed class TestIngestionBatchConsumer : IIngestionBatchConsumer
    {
        private readonly Channel<IngestionBatchDelivery> _deliveries =
            Channel.CreateUnbounded<IngestionBatchDelivery>();

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int AcknowledgementCount { get; private set; }

        public IAsyncEnumerable<IngestionBatchDelivery> ReadAllAsync(
            CancellationToken cancellationToken)
        {
            Started.SetResult();
            return _deliveries.Reader.ReadAllAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            _deliveries.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public async Task DeliverAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        {
            var acknowledged = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var delivery = new IngestionBatchDelivery(
                body,
                acknowledgementCancellationToken =>
                {
                    AcknowledgementCount++;
                    acknowledged.SetResult();
                    return Task.CompletedTask;
                });

            await _deliveries.Writer.WriteAsync(delivery, cancellationToken);
            await acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        {
            var delivery = new IngestionBatchDelivery(
                body,
                acknowledgementCancellationToken =>
                {
                    AcknowledgementCount++;
                    return Task.CompletedTask;
                });

            return _deliveries.Writer.WriteAsync(delivery, cancellationToken);
        }
    }

    private sealed class RecordingEventChunkStore : IEventChunkStore
    {
        private readonly Exception? _exception;

        public RecordingEventChunkStore(Exception? exception = null)
        {
            _exception = exception;
        }

        public List<IReadOnlyList<EventEnvelope>> SuccessfulChunks { get; } = [];

        public TaskCompletionSource StoreAttempted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StoreAsync(
            IReadOnlyCollection<EventEnvelope> events,
            CancellationToken cancellationToken = default)
        {
            StoreAttempted.SetResult();

            if (_exception is not null)
            {
                throw _exception;
            }

            SuccessfulChunks.Add(events.ToArray());
            return Task.CompletedTask;
        }
    }

    #endregion
}
