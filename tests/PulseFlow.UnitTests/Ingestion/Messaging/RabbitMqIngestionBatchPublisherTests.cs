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

    #endregion
}
