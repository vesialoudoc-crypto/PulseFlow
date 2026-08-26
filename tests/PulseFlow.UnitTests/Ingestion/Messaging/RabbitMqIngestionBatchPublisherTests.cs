using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using PulseFlow.Api.Ingestion.Messaging;
using PulseFlow.Api.Ingestion.Messaging.RabbitMq;
using RabbitMQ.Client;

namespace PulseFlow.UnitTests.Ingestion.Messaging;

public sealed class RabbitMqIngestionBatchPublisherTests
{
    [Fact]
    public async Task PublishAsync_FiveConcurrentPublishes_UsesFourExclusiveConfirmationChannels()
    {
        // Arrange
        var channelFactory = new BlockingPublisherChannelFactory();
        await using var publisher = new RabbitMqIngestionBatchPublisher(
            channelFactory.CreateChannelAsync,
            new RabbitMqOptions
            {
                QueueName = "test-queue",
                PublisherChannelCount = 4,
            },
            NullLogger<RabbitMqIngestionBatchPublisher>.Instance);
        await publisher.InitializeAsync(CancellationToken.None);
        var firstFourPublishes = Enumerable.Range(0, 4)
            .Select(_ => publisher.PublishAsync(ReadOnlyMemory<byte>.Empty, CancellationToken.None))
            .ToArray();
        await channelFactory.FourPublishesStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var fifthPublishCancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act
        var fifthPublish = publisher.PublishAsync(
            ReadOnlyMemory<byte>.Empty,
            fifthPublishCancellation.Token);
        var cancellationException = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fifthPublish);
        channelFactory.AllowPublishesToComplete();
        await Task.WhenAll(firstFourPublishes);

        // Assert
        Assert.Equal(4, channelFactory.CreatedChannels.Count);
        Assert.Equal(fifthPublishCancellation.Token, cancellationException.CancellationToken);
        Assert.All(channelFactory.ChannelOptions, static options =>
        {
            Assert.True(options.PublisherConfirmationsEnabled);
            Assert.True(options.PublisherConfirmationTrackingEnabled);
        });
        Assert.All(channelFactory.CreatedChannels, static channel =>
        {
            Assert.Equal(1, channel.MaximumConcurrentPublishes);
            Assert.True(channel.MandatoryPublishUsed);
        });
    }

    [Fact]
    public async Task PublishAsync_PublishConfirmationExceedsInternalTimeout_ThrowsTimeoutException()
    {
        // Arrange
        var channelFactory = new BlockingPublisherChannelFactory();
        await using var publisher = new RabbitMqIngestionBatchPublisher(
            channelFactory.CreateChannelAsync,
            new RabbitMqOptions
            {
                QueueName = "test-queue",
                PublisherChannelCount = 1,
                PublishConfirmationTimeout = TimeSpan.FromMilliseconds(25),
            },
            NullLogger<RabbitMqIngestionBatchPublisher>.Instance);
        await publisher.InitializeAsync(CancellationToken.None);

        // Act
        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            publisher.PublishAsync(ReadOnlyMemory<byte>.Empty, CancellationToken.None));

        // Assert
        Assert.Equal("RabbitMQ publish/confirmation timed out.", exception.Message);
    }

    [Fact]
    public async Task PublishAsync_PublishTimesOut_DisposesUncertainChannel()
    {
        // Arrange
        var channelFactory = new ScriptedPublisherChannelFactory();
        channelFactory.EnqueueChannel(PublishOutcome.BlockUntilCanceled);
        await using var publisher = CreatePublisher(channelFactory);
        await publisher.InitializeAsync(CancellationToken.None);

        // Act
        await Assert.ThrowsAsync<TimeoutException>(() =>
            publisher.PublishAsync("timed-out-batch"u8.ToArray(), CancellationToken.None));

        // Assert
        Assert.True(channelFactory.CreatedChannels.Single().Disposed);
        Assert.False(publisher.HasUsableChannel);
    }

    [Fact]
    public async Task PublishAsync_PublishTimeoutWithBlockingCleanup_ThrowsWithoutWaitingForCleanup()
    {
        // Arrange
        var channelFactory = new ScriptedPublisherChannelFactory();
        channelFactory.EnqueueChannel(PublishOutcome.BlockUntilCanceled, DisposalOutcome.Block);
        await using var publisher = CreatePublisher(channelFactory);
        await publisher.InitializeAsync(CancellationToken.None);

        // Act
        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            publisher.PublishAsync("timed-out-batch"u8.ToArray(), CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(1));

        // Assert
        Assert.Equal("RabbitMQ publish/confirmation timed out.", exception.Message);
        Assert.True(channelFactory.CreatedChannels.Single().DisposalStarted);
        channelFactory.CreatedChannels.Single().CompleteDisposal();
    }

    [Fact]
    public async Task PublishAsync_CallerCancellationWithBlockingCleanup_ReturnsCancellationWithoutWaitingForCleanup()
    {
        // Arrange
        var channelFactory = new ScriptedPublisherChannelFactory();
        channelFactory.EnqueueChannel(PublishOutcome.BlockUntilCanceled, DisposalOutcome.Block);
        await using var publisher = CreatePublisher(channelFactory);
        await publisher.InitializeAsync(CancellationToken.None);
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));

        // Act
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            publisher.PublishAsync("cancelled-batch"u8.ToArray(), cancellationSource.Token)).WaitAsync(TimeSpan.FromSeconds(1));

        // Assert
        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
        Assert.True(channelFactory.CreatedChannels.Single().DisposalStarted);
        channelFactory.CreatedChannels.Single().CompleteDisposal();
    }

    [Fact]
    public async Task PublishAsync_UncertainPublish_DoesNotReuseChannelOrRetryBatch()
    {
        // Arrange
        var channelFactory = new ScriptedPublisherChannelFactory();
        channelFactory.EnqueueChannel(PublishOutcome.BlockUntilCanceled);
        channelFactory.EnqueueChannel(PublishOutcome.Succeed);
        await using var publisher = CreatePublisher(channelFactory);
        await publisher.InitializeAsync(CancellationToken.None);

        // Act
        await Assert.ThrowsAsync<TimeoutException>(() =>
            publisher.PublishAsync("first-batch"u8.ToArray(), CancellationToken.None));
        await publisher.PublishAsync("second-batch"u8.ToArray(), CancellationToken.None);

        // Assert
        Assert.Equal(2, channelFactory.CreatedChannels.Count);
        Assert.Equal(1, channelFactory.CreatedChannels[0].PublishCount);
        Assert.Equal(1, channelFactory.CreatedChannels[1].PublishCount);
        Assert.True(channelFactory.CreatedChannels[0].Disposed);
        Assert.False(channelFactory.CreatedChannels[1].Disposed);
    }

    [Fact]
    public async Task PublishAsync_RepeatedTimeouts_LeavesSlotRecoverableForLaterPublish()
    {
        // Arrange
        var channelFactory = new ScriptedPublisherChannelFactory();
        channelFactory.EnqueueChannel(PublishOutcome.BlockUntilCanceled);
        channelFactory.EnqueueChannel(PublishOutcome.BlockUntilCanceled);
        channelFactory.EnqueueChannel(PublishOutcome.Succeed);
        await using var publisher = CreatePublisher(channelFactory);
        await publisher.InitializeAsync(CancellationToken.None);

        // Act
        await Assert.ThrowsAsync<TimeoutException>(() => publisher.PublishAsync(ReadOnlyMemory<byte>.Empty, CancellationToken.None));
        await Assert.ThrowsAsync<TimeoutException>(() => publisher.PublishAsync(ReadOnlyMemory<byte>.Empty, CancellationToken.None));
        await publisher.PublishAsync(ReadOnlyMemory<byte>.Empty, CancellationToken.None);

        // Assert
        Assert.Equal(3, channelFactory.CreatedChannels.Count);
        Assert.Equal(1, channelFactory.CreatedChannels[2].PublishCount);
        Assert.True(publisher.HasUsableChannel);
    }

    [Fact]
    public async Task PublishAsync_CallerCancellation_DoesNotRetryBatchAndLeavesSlotRecoverable()
    {
        // Arrange
        var channelFactory = new ScriptedPublisherChannelFactory();
        channelFactory.EnqueueChannel(PublishOutcome.BlockUntilCanceled);
        channelFactory.EnqueueChannel(PublishOutcome.Succeed);
        await using var publisher = CreatePublisher(channelFactory);
        await publisher.InitializeAsync(CancellationToken.None);
        using var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(25));

        // Act
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            publisher.PublishAsync("cancelled-batch"u8.ToArray(), cancellationSource.Token));
        await publisher.PublishAsync("later-batch"u8.ToArray(), CancellationToken.None);

        // Assert
        Assert.Equal(cancellationSource.Token, exception.CancellationToken);
        Assert.Equal(1, channelFactory.CreatedChannels[0].PublishCount);
        Assert.Equal(1, channelFactory.CreatedChannels[1].PublishCount);
        Assert.True(publisher.HasUsableChannel);
    }

    [Fact]
    public async Task PublishAsync_ReplacementCreationFails_LeavesSlotRecoverableForLaterPublish()
    {
        // Arrange
        var channelFactory = new ScriptedPublisherChannelFactory();
        channelFactory.EnqueueChannel(PublishOutcome.BlockUntilCanceled);
        channelFactory.EnqueueFailure(new InvalidOperationException("Replacement channel creation failed."));
        channelFactory.EnqueueChannel(PublishOutcome.Succeed);
        await using var publisher = CreatePublisher(channelFactory);
        await publisher.InitializeAsync(CancellationToken.None);

        // Act
        await Assert.ThrowsAsync<TimeoutException>(() => publisher.PublishAsync(ReadOnlyMemory<byte>.Empty, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(ReadOnlyMemory<byte>.Empty, CancellationToken.None));
        await publisher.PublishAsync(ReadOnlyMemory<byte>.Empty, CancellationToken.None);

        // Assert
        Assert.Equal(2, channelFactory.CreatedChannels.Count);
        Assert.Equal(1, channelFactory.CreateFailureCount);
        Assert.True(publisher.HasUsableChannel);
    }

    [Fact]
    public async Task CheckHealthAsync_AllPublisherChannelsAreUncertain_ReturnsUnhealthy()
    {
        // Arrange
        var channelFactory = new ScriptedPublisherChannelFactory();
        channelFactory.EnqueueChannel(PublishOutcome.BlockUntilCanceled);
        await using var publisher = CreatePublisher(channelFactory);
        await publisher.InitializeAsync(CancellationToken.None);
        await Assert.ThrowsAsync<TimeoutException>(() => publisher.PublishAsync(ReadOnlyMemory<byte>.Empty, CancellationToken.None));
        var connectionManager = new RabbitMqConnectionManager(
            "amqp://guest:guest@localhost:5672/",
            new RabbitMqOptions { QueueName = "test-queue" },
            NullLogger<RabbitMqConnectionManager>.Instance);
        var connection = DispatchProxy.Create<IConnection, OpenRabbitMqConnection>();
        typeof(RabbitMqConnectionManager)
            .GetField("_connection", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(connectionManager, connection);
        var healthCheck = new RabbitMqHealthCheck(connectionManager, publisher);

        // Act
        var result = await healthCheck.CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());

        // Assert
        Assert.Equal(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task DisposeAsync_InitializedPublisher_DisposesEveryPooledChannel()
    {
        // Arrange
        var channelFactory = new BlockingPublisherChannelFactory();
        var publisher = new RabbitMqIngestionBatchPublisher(
            channelFactory.CreateChannelAsync,
            new RabbitMqOptions
            {
                QueueName = "test-queue",
                PublisherChannelCount = 4,
            },
            NullLogger<RabbitMqIngestionBatchPublisher>.Instance);
        await publisher.InitializeAsync(CancellationToken.None);

        // Act
        await publisher.DisposeAsync();

        // Assert
        Assert.Equal(4, channelFactory.CreatedChannels.Count);
        Assert.All(channelFactory.CreatedChannels, static channel => Assert.True(channel.Disposed));
    }

    #region Test helpers

    internal sealed class BlockingPublisherChannelFactory
    {
        private readonly object _syncRoot = new();
        private readonly TaskCompletionSource _allowPublishesToComplete = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _startedPublishCount;

        public List<CreateChannelOptions> ChannelOptions { get; } = [];

        public List<BlockingPublisherChannel> CreatedChannels { get; } = [];

        public TaskCompletionSource FourPublishesStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<IChannel> CreateChannelAsync(
            CreateChannelOptions channelOptions,
            CancellationToken cancellationToken)
        {
            var channel = DispatchProxy.Create<IChannel, BlockingPublisherChannel>();
            var channelProxy = (BlockingPublisherChannel)(object)channel;
            channelProxy.Initialize(this);

            lock (_syncRoot)
            {
                ChannelOptions.Add(channelOptions);
                CreatedChannels.Add(channelProxy);
            }

            return ValueTask.FromResult(channel);
        }

        public void AllowPublishesToComplete()
        {
            _allowPublishesToComplete.TrySetResult();
        }

        internal Task PublishAsync(
            BlockingPublisherChannel channel,
            bool mandatory,
            CancellationToken cancellationToken)
        {
            channel.StartPublish(mandatory);

            if (Interlocked.Increment(ref _startedPublishCount) == 4)
            {
                FourPublishesStarted.TrySetResult();
            }

            return _allowPublishesToComplete.Task.WaitAsync(cancellationToken);
        }
    }

    public class BlockingPublisherChannel : DispatchProxy
    {
        private BlockingPublisherChannelFactory? _factory;
        private int _concurrentPublishes;

        public bool Disposed { get; private set; }

        public bool MandatoryPublishUsed { get; private set; }

        public int MaximumConcurrentPublishes { get; private set; }

        internal void Initialize(BlockingPublisherChannelFactory factory)
        {
            _factory = factory;
        }

        public void StartPublish(bool mandatory)
        {
            MandatoryPublishUsed = mandatory;
            var concurrentPublishes = Interlocked.Increment(ref _concurrentPublishes);
            MaximumConcurrentPublishes = Math.Max(MaximumConcurrentPublishes, concurrentPublishes);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? arguments)
        {
            return targetMethod?.Name switch
            {
                "BasicPublishAsync" => new ValueTask(
                    _factory!.PublishAsync(
                        this,
                        (bool)arguments![2]!,
                        arguments.OfType<CancellationToken>().Single()
                    )
                ),
                "DisposeAsync" => Dispose(),
                _ => throw new NotSupportedException($"Unexpected channel call: {targetMethod?.Name}."),
            };
        }

        private ValueTask Dispose()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private static RabbitMqIngestionBatchPublisher CreatePublisher(ScriptedPublisherChannelFactory channelFactory)
    {
        return new RabbitMqIngestionBatchPublisher(
            channelFactory.CreateChannelAsync,
            new RabbitMqOptions
            {
                QueueName = "test-queue",
                PublisherChannelCount = 1,
                PublisherChannelTimeout = TimeSpan.FromMilliseconds(100),
                PublishConfirmationTimeout = TimeSpan.FromMilliseconds(25),
            },
            NullLogger<RabbitMqIngestionBatchPublisher>.Instance);
    }

    private enum PublishOutcome
    {
        Succeed,
        BlockUntilCanceled,
    }

    private enum DisposalOutcome
    {
        Complete,
        Block,
    }

    private sealed class ScriptedPublisherChannelFactory
    {
        private readonly Queue<Func<ValueTask<IChannel>>> _channelCreations = new();

        public List<ScriptedPublisherChannel> CreatedChannels { get; } = [];

        public int CreateFailureCount { get; private set; }

        public void EnqueueChannel(PublishOutcome outcome, DisposalOutcome disposalOutcome = DisposalOutcome.Complete)
        {
            _channelCreations.Enqueue(() =>
            {
                var channel = DispatchProxy.Create<IChannel, ScriptedPublisherChannel>();
                var channelProxy = (ScriptedPublisherChannel)(object)channel;
                channelProxy.Initialize(outcome, disposalOutcome);
                CreatedChannels.Add(channelProxy);
                return ValueTask.FromResult(channel);
            });
        }

        public void EnqueueFailure(Exception exception)
        {
            _channelCreations.Enqueue(() =>
            {
                CreateFailureCount++;
                return ValueTask.FromException<IChannel>(exception);
            });
        }

        public ValueTask<IChannel> CreateChannelAsync(
            CreateChannelOptions channelOptions,
            CancellationToken cancellationToken)
        {
            return _channelCreations.Dequeue().Invoke();
        }
    }

    private class ScriptedPublisherChannel : DispatchProxy
    {
        private PublishOutcome _outcome;
        private DisposalOutcome _disposalOutcome;
        private readonly TaskCompletionSource _disposeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public bool DisposalStarted { get; private set; }

        public void CompleteDisposal()
        {
            _disposeCompletion.TrySetResult();
        }

        public int PublishCount { get; private set; }

        public void Initialize(PublishOutcome outcome, DisposalOutcome disposalOutcome)
        {
            _outcome = outcome;
            _disposalOutcome = disposalOutcome;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? arguments)
        {
            return targetMethod?.Name switch
            {
                "BasicPublishAsync" => new ValueTask(PublishAsync(arguments!.OfType<CancellationToken>().Single())),
                "DisposeAsync" => Dispose(),
                _ => throw new NotSupportedException($"Unexpected channel call: {targetMethod?.Name}."),
            };
        }

        private Task PublishAsync(CancellationToken cancellationToken)
        {
            PublishCount++;

            return _outcome switch
            {
                PublishOutcome.Succeed => Task.CompletedTask,
                PublishOutcome.BlockUntilCanceled => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken),
                _ => throw new InvalidOperationException("Unsupported publish outcome."),
            };
        }

        private ValueTask Dispose()
        {
            Disposed = true;
            DisposalStarted = true;

            return _disposalOutcome switch
            {
                DisposalOutcome.Complete => ValueTask.CompletedTask,
                DisposalOutcome.Block => new ValueTask(_disposeCompletion.Task),
                _ => throw new InvalidOperationException("Unsupported disposal outcome."),
            };
        }
    }

    private class OpenRabbitMqConnection : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? arguments)
        {
            return targetMethod?.Name switch
            {
                "get_IsOpen" => true,
                _ => throw new NotSupportedException($"Unexpected connection call: {targetMethod?.Name}."),
            };
        }
    }

    #endregion
}
