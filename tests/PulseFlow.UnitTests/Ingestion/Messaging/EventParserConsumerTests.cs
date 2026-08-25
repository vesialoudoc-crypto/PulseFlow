using System.Text;
using System.Text.Json;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PulseFlow.Api.Ingestion;
using PulseFlow.Api.Ingestion.Contracts;
using PulseFlow.Api.Ingestion.Messaging;
using PulseFlow.Api.Ingestion.Messaging.RabbitMq;
using PulseFlow.Api.Ingestion.Ndjson;
using PulseFlow.Api.Ingestion.Persistence;
using PulseFlow.Api.Ingestion.Validation;
using PulseFlow.Api.Startup;
using RabbitMQ.Client;

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
        Assert.Equal(1, testContext.Consumer.AcknowledgementCount);
        Assert.Equal(0, testContext.Consumer.RejectionCount);
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
        Assert.Equal(0, testContext.Consumer.RejectionCount);
    }

    [Fact]
    public async Task EventParserConsumer_ProcessingFailure_RejectsDelivery()
    {
        // Arrange
        var store = new RecordingEventChunkStore(new InvalidOperationException("Persistence failed."));
        await using var testContext = CreateTestContext(store);

        // Act
        await testContext.DeliverAsync(Encoding.UTF8.GetBytes(CreateRecordJson("valid") + "\n"));

        // Assert
        Assert.Equal(1, store.StoreAsyncAttemptCount);
        Assert.Equal(0, testContext.Consumer.AcknowledgementCount);
        Assert.Equal(1, testContext.Consumer.RejectionCount);
    }

    [Fact]
    public async Task EventParserConsumer_AcknowledgementFails_DoesNotRejectDelivery()
    {
        // Arrange
        var store = new RecordingEventChunkStore();
        await using var testContext = CreateTestContext(store);
        testContext.Consumer.AcknowledgementException = new InvalidOperationException("Acknowledgement failed.");

        // Act
        await testContext.SendAsync(Encoding.UTF8.GetBytes(CreateRecordJson("valid") + "\n"));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => testContext.ExecutionTask.WaitAsync(TimeSpan.FromSeconds(5)));

        // Assert
        Assert.Equal("Acknowledgement failed.", exception.Message);
        Assert.Equal(1, testContext.Consumer.AcknowledgementCount);
        Assert.Equal(0, testContext.Consumer.RejectionCount);
    }

    [Fact]
    public async Task EventParserConsumer_RejectionFails_DoesNotAcknowledgeDelivery()
    {
        // Arrange
        var store = new RecordingEventChunkStore(new InvalidOperationException("Persistence failed."));
        await using var testContext = CreateTestContext(store);
        testContext.Consumer.RejectionException = new InvalidOperationException("Rejection failed.");

        // Act
        await testContext.SendAsync(Encoding.UTF8.GetBytes(CreateRecordJson("valid") + "\n"));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => testContext.ExecutionTask.WaitAsync(TimeSpan.FromSeconds(5)));

        // Assert
        Assert.Equal("Rejection failed.", exception.Message);
        Assert.Equal(0, testContext.Consumer.AcknowledgementCount);
        Assert.Equal(1, testContext.Consumer.RejectionCount);
    }

    [Fact]
    public async Task EventParserConsumer_ProcessingIsCancelled_DoesNotRejectDelivery()
    {
        // Arrange
        var store = new RecordingEventChunkStore(waitForCancellation: true);
        await using var testContext = CreateTestContext(store);

        // Act
        await testContext.SendAsync(Encoding.UTF8.GetBytes(CreateRecordJson("valid") + "\n"));
        await store.StoreAttempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await testContext.StopAsync();

        // Assert
        Assert.Equal(1, store.StoreAsyncAttemptCount);
        Assert.Equal(0, testContext.Consumer.AcknowledgementCount);
        Assert.Equal(0, testContext.Consumer.RejectionCount);
    }

    [Fact]
    public async Task EventParserConsumer_TwoWorkersHostStops_CancelsWorkers()
    {
        // Arrange
        var store = new RecordingEventChunkStore();
        var firstWorker = new TestIngestionBatchConsumer();
        var secondWorker = new TestIngestionBatchConsumer();
        await using var testContext = CreateWorkerTestContext(
            store,
            new TestIngestionBatchConsumerFactory(firstWorker, secondWorker),
            consumerCount: 2);

        await testContext.StartAsync();
        await Task.WhenAll(firstWorker.Started.Task, secondWorker.Started.Task)
            .WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        await testContext.StopAsync();

        // Assert
        Assert.True(testContext.ExecutionTask.IsCanceled);
        await Task.WhenAll(firstWorker.Disposed.Task, secondWorker.Disposed.Task)
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task EventParserConsumer_StartupInitializationIncomplete_DoesNotCreateConsumerChannelUntilInitializationCompletes()
    {
        // Arrange
        var readinessState = new StartupReadinessState();
        var store = new RecordingEventChunkStore();
        var worker = new TestIngestionBatchConsumer();
        await using var testContext = CreateWorkerTestContext(
            store,
            new TestIngestionBatchConsumerFactory(worker),
            consumerCount: 1,
            readinessState: readinessState);
        await testContext.StartAsync();

        // Act
        var workerStartedBeforeInitializationCompleted = worker.Started.Task.IsCompleted;
        readinessState.MarkInitializationCompleted();
        await worker.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        Assert.False(workerStartedBeforeInitializationCompleted);
    }

    [Fact]
    public async Task RabbitMqIngestionBatchConsumer_CancelFails_StillDisposesChannel()
    {
        // Arrange
        var channel = DispatchProxy.Create<IChannel, CancelFailingChannel>();
        var consumer = new RabbitMqIngestionBatchConsumer(channel, "test-queue");
        using var stoppingSource = new CancellationTokenSource();
        var consumptionTask = consumer.ConsumeAsync(
            static (_, _) => Task.CompletedTask,
            stoppingSource.Token);
        await ((CancelFailingChannel)(object)channel).ConsumeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => consumer.DisposeAsync().AsTask());

        // Assert
        Assert.Equal("Consumer cancellation failed.", exception.Message);
        Assert.True(((CancelFailingChannel)(object)channel).Disposed);
        stoppingSource.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => consumptionTask);
    }

    #region Test helpers

    private static ConsumerTestContext CreateTestContext(RecordingEventChunkStore store)
    {
        return new ConsumerTestContext(CreateServiceProvider(store), new TestIngestionBatchConsumer());
    }

    private static WorkerTestContext CreateWorkerTestContext(
        RecordingEventChunkStore store,
        IIngestionBatchConsumerFactory consumerFactory,
        int consumerCount,
        StartupReadinessState? readinessState = null)
    {
        return new WorkerTestContext(
            CreateServiceProvider(store),
            consumerFactory,
            consumerCount,
            readinessState ?? CreateReadyState());
    }

    private static ServiceProvider CreateServiceProvider(RecordingEventChunkStore store)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new EventEnvelopeValidator());
        services.AddSingleton<NdjsonRecordReader>();
        services.AddScoped<IEventChunkStore>(_ => store);
        services.AddScoped<IngestEventsHandler>(serviceProvider => new IngestEventsHandler(
            serviceProvider.GetRequiredService<EventEnvelopeValidator>(),
            serviceProvider.GetRequiredService<IEventChunkStore>(),
            chunkCapacity: 10));

        return services.BuildServiceProvider();
    }

    private static string CreateRecordJson(string type, object? payload = null)
    {
        return JsonSerializer.Serialize(new
        {
            eventId = Guid.NewGuid(),
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
            var readinessState = CreateReadyState();
            _eventParserConsumer = new EventParserConsumer(
                new TestIngestionBatchConsumerFactory(Consumer),
                _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                _serviceProvider.GetRequiredService<NdjsonRecordReader>(),
                NullLogger<EventParserConsumer>.Instance,
                consumerCount: 1,
                readinessState);
        }

        public TestIngestionBatchConsumer Consumer { get; }

        public Task ExecutionTask => _eventParserConsumer.ExecuteTask
            ?? throw new InvalidOperationException("The event parser consumer has not started.");

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
            await StopAsync();
            _stoppingSource.Dispose();
            await _serviceProvider.DisposeAsync();
        }

        public async Task StopAsync()
        {
            _stoppingSource.Cancel();
            await _eventParserConsumer.StopAsync(CancellationToken.None);
        }
    }

    private sealed class TestIngestionBatchConsumerFactory : IIngestionBatchConsumerFactory
    {
        private readonly IReadOnlyList<TestIngestionBatchConsumer> _consumers;
        private int _nextConsumer;

        public TestIngestionBatchConsumerFactory(params TestIngestionBatchConsumer[] consumers)
        {
            _consumers = consumers;
        }

        public ValueTask<IIngestionBatchConsumer> CreateAsync(
            CancellationToken cancellationToken)
        {
            var consumerIndex = Interlocked.Increment(ref _nextConsumer) - 1;
            return ValueTask.FromResult<IIngestionBatchConsumer>(_consumers[consumerIndex]);
        }
    }

    private sealed class TestIngestionBatchConsumer : IIngestionBatchConsumer
    {
        private readonly TaskCompletionSource _consumptionFailure = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private IngestionBatchHandler? _handler;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int AcknowledgementCount { get; private set; }

        public int RejectionCount { get; private set; }

        public Exception? AcknowledgementException { get; set; }

        public Exception? RejectionException { get; set; }

        public async Task ConsumeAsync(
            IngestionBatchHandler handler,
            CancellationToken cancellationToken)
        {
            _handler = handler;
            Started.SetResult();

            var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var completedTask = await Task.WhenAny(_consumptionFailure.Task, cancellationTask);
            await completedTask;
        }

        public Task WaitUntilStartedAsync(CancellationToken cancellationToken)
        {
            return Started.Task.WaitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }

        public async Task DeliverAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        {
            var completed = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var delivery = new IngestionBatchDelivery(
                body,
                _ =>
                {
                    AcknowledgementCount++;
                    completed.SetResult();
                    return AcknowledgementException is null
                        ? Task.CompletedTask
                        : Task.FromException(AcknowledgementException);
                },
                _ =>
                {
                    RejectionCount++;
                    completed.SetResult();
                    return RejectionException is null
                        ? Task.CompletedTask
                        : Task.FromException(RejectionException);
                });

            await DispatchAsync(delivery, cancellationToken);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> body, CancellationToken cancellationToken)
        {
            var delivery = new IngestionBatchDelivery(
                body,
                acknowledgementCancellationToken =>
                {
                    AcknowledgementCount++;
                    return AcknowledgementException is null
                        ? Task.CompletedTask
                        : Task.FromException(AcknowledgementException);
                },
                rejectionCancellationToken =>
                {
                    RejectionCount++;
                    return RejectionException is null
                        ? Task.CompletedTask
                        : Task.FromException(RejectionException);
                });

            _ = DispatchAsync(delivery, cancellationToken);
            return ValueTask.CompletedTask;
        }

        private async Task DispatchAsync(
            IngestionBatchDelivery delivery,
            CancellationToken cancellationToken)
        {
            var handler = _handler
                ?? throw new InvalidOperationException("The consumer has not started.");

            try
            {
                await handler(delivery, cancellationToken);
            }
            catch (Exception exception)
            {
                _consumptionFailure.TrySetException(exception);
            }
        }
    }

    private sealed class WorkerTestContext : IAsyncDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly CancellationTokenSource _stoppingSource = new();
        private readonly EventParserConsumer _eventParserConsumer;

        public WorkerTestContext(
            ServiceProvider serviceProvider,
            IIngestionBatchConsumerFactory consumerFactory,
            int consumerCount,
            StartupReadinessState readinessState)
        {
            _serviceProvider = serviceProvider;
            _eventParserConsumer = new EventParserConsumer(
                consumerFactory,
                _serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                _serviceProvider.GetRequiredService<NdjsonRecordReader>(),
                NullLogger<EventParserConsumer>.Instance,
                consumerCount,
                readinessState);
        }

        public Task ExecutionTask => _eventParserConsumer.ExecuteTask
            ?? throw new InvalidOperationException("The event parser consumer has not started.");

        public Task StartAsync()
        {
            return _eventParserConsumer.StartAsync(_stoppingSource.Token);
        }

        public async Task StopAsync()
        {
            _stoppingSource.Cancel();
            await _eventParserConsumer.StopAsync(CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            _stoppingSource.Dispose();
            await _serviceProvider.DisposeAsync();
        }
    }

    private static StartupReadinessState CreateReadyState()
    {
        var readinessState = new StartupReadinessState();
        readinessState.MarkReady();
        return readinessState;
    }

    public class CancelFailingChannel : DispatchProxy
    {
        public TaskCompletionSource ConsumeStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? arguments)
        {
            switch (targetMethod?.Name)
            {
                case "BasicConsumeAsync":
                    ConsumeStarted.TrySetResult();
                    return Task.FromResult("consumer-tag");
                case "BasicCancelAsync":
                    return Task.FromException(new InvalidOperationException("Consumer cancellation failed."));
                case "DisposeAsync":
                    Disposed = true;
                    return ValueTask.CompletedTask;
                default:
                    throw new NotSupportedException($"Unexpected channel call: {targetMethod?.Name}.");
            }
        }
    }

    private sealed class RecordingEventChunkStore : IEventChunkStore
    {
        private readonly Queue<Exception?> _attemptExceptions;
        private readonly bool _waitForCancellation;

        public RecordingEventChunkStore(
            params Exception?[] attemptExceptions)
            : this(attemptExceptions, waitForCancellation: false)
        {
        }

        public RecordingEventChunkStore(bool waitForCancellation)
            : this([], waitForCancellation)
        {
        }

        private RecordingEventChunkStore(
            IEnumerable<Exception?> attemptExceptions,
            bool waitForCancellation = false)
        {
            _attemptExceptions = new Queue<Exception?>(attemptExceptions);
            _waitForCancellation = waitForCancellation;
        }

        public int StoreAsyncAttemptCount { get; private set; }

        public List<IReadOnlyList<EventEnvelope>> SuccessfulChunks { get; } = [];

        public TaskCompletionSource StoreAttempted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task StoreAsync(
            IReadOnlyCollection<EventEnvelope> events,
            CancellationToken cancellationToken = default)
        {
            StoreAsyncAttemptCount++;
            StoreAttempted.TrySetResult();

            if (_waitForCancellation)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (_attemptExceptions.TryDequeue(out var exception) && exception is not null)
            {
                throw exception;
            }

            SuccessfulChunks.Add(events.ToArray());
        }
    }

    #endregion
}
