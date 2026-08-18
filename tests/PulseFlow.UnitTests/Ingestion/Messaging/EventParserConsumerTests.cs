using System.Text;
using System.Text.Json;
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
        var delivery = new RabbitMqDelivery(1, Encoding.UTF8.GetBytes(
            CreateRecordJson("first") + "\n" + CreateRecordJson("second") + "\n"));

        // Act
        await testContext.DeliverAsync(delivery);

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
        var delivery = new RabbitMqDelivery(1, Encoding.UTF8.GetBytes(rawBatch));

        // Act
        await testContext.DeliverAsync(delivery);

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
        var delivery = new RabbitMqDelivery(42, Encoding.UTF8.GetBytes(CreateRecordJson("valid") + "\n"));

        // Act
        await testContext.DeliverAsync(delivery);

        // Assert
        Assert.Equal(new ulong[] { 42 }, testContext.ConsumerChannel.AcknowledgedDeliveryTags);
    }

    [Fact]
    public async Task EventParserConsumer_ProcessingFails_DoesNotAcknowledgeDelivery()
    {
        // Arrange
        var store = new RecordingEventChunkStore(new InvalidOperationException("Persistence failed."));
        await using var testContext = CreateTestContext(store);
        var delivery = new RabbitMqDelivery(42, Encoding.UTF8.GetBytes(CreateRecordJson("valid") + "\n"));

        // Act
        await testContext.DeliverAsync(delivery);

        // Assert
        Assert.Empty(testContext.ConsumerChannel.AcknowledgedDeliveryTags);
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

        return new ConsumerTestContext(services.BuildServiceProvider(), new TestRabbitMqConsumerChannel());
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
            TestRabbitMqConsumerChannel consumerChannel)
        {
            _serviceProvider = serviceProvider;
            ConsumerChannel = consumerChannel;
            _eventParserConsumer = new EventParserConsumer(
                ConsumerChannel,
                _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                _serviceProvider.GetRequiredService<NdjsonRecordReader>(),
                NullLogger<EventParserConsumer>.Instance);
        }

        public TestRabbitMqConsumerChannel ConsumerChannel { get; }

        public async Task DeliverAsync(RabbitMqDelivery delivery)
        {
            await _eventParserConsumer.StartAsync(_stoppingSource.Token);
            await ConsumerChannel.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await ConsumerChannel.DeliverAsync(delivery, _stoppingSource.Token);
        }

        public async ValueTask DisposeAsync()
        {
            _stoppingSource.Cancel();
            await _eventParserConsumer.StopAsync(CancellationToken.None);
            _stoppingSource.Dispose();
            await _serviceProvider.DisposeAsync();
        }
    }

    private sealed class TestRabbitMqConsumerChannel : IRabbitMqConsumerChannel
    {
        private Func<RabbitMqDelivery, CancellationToken, Task>? _deliveryHandler;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<ulong> AcknowledgedDeliveryTags { get; } = [];

        public Task StartConsumingAsync(
            Func<RabbitMqDelivery, CancellationToken, Task> deliveryHandler,
            CancellationToken cancellationToken)
        {
            _deliveryHandler = deliveryHandler;
            Started.SetResult();
            return Task.CompletedTask;
        }

        public Task AcknowledgeAsync(
            ulong deliveryTag,
            CancellationToken cancellationToken)
        {
            AcknowledgedDeliveryTags.Add(deliveryTag);
            return Task.CompletedTask;
        }

        public Task StopConsumingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task DeliverAsync(RabbitMqDelivery delivery, CancellationToken cancellationToken)
        {
            return (_deliveryHandler ?? throw new InvalidOperationException("Consumer has not started."))(
                delivery,
                cancellationToken);
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

        public Task StoreAsync(
            IReadOnlyCollection<EventEnvelope> events,
            CancellationToken cancellationToken = default)
        {
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
