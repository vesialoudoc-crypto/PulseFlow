using System.Threading.Channels;
using RabbitMQ.Client;

namespace PulseFlow.Api.Ingestion.Messaging.RabbitMq;

internal sealed class RabbitMqIngestionBatchPublisher : IIngestionBatchPublisher, IAsyncDisposable
{
    private readonly Func<CreateChannelOptions, CancellationToken, ValueTask<IChannel>> _createChannelAsync;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqIngestionBatchPublisher> _logger;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private Channel<IChannel>? _publisherChannels;
    private bool _isDisposed;

    internal RabbitMqIngestionBatchPublisher(
        Func<CreateChannelOptions, CancellationToken, ValueTask<IChannel>> createChannelAsync,
        RabbitMqOptions options,
        ILogger<RabbitMqIngestionBatchPublisher> logger
    )
    {
        _createChannelAsync = createChannelAsync;
        _options = options;
        _logger = logger;
    }

    public RabbitMqIngestionBatchPublisher(
        RabbitMqConnectionManager connectionManager,
        RabbitMqOptions options,
        ILogger<RabbitMqIngestionBatchPublisher> logger
    )
        : this((channelOptions, ct) => connectionManager.CreateChannelAsync(channelOptions, ct), options, logger) { }

    public async Task InitializeAsync(CancellationToken ct)
    {
        // Startup and the first HTTP request can race, but the pool is created once.
        await _initializationGate.WaitAsync(ct);

        try
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (_publisherChannels is not null)
            {
                return;
            }

            var channelOptions = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true
            );
            var channels = new List<IChannel>(_options.PublisherChannelCount);

            try
            {
                for (var channelIndex = 0; channelIndex < _options.PublisherChannelCount; channelIndex++)
                {
                    using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeoutSource.CancelAfter(_options.PublisherChannelTimeout);

                    try
                    {
                        channels.Add(await _createChannelAsync(channelOptions, timeoutSource.Token));
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(ct);
                    }
                    catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
                    {
                        _logger.LogWarning(
                            "RabbitMQ publisher channel creation timed out after {ConfiguredTimeout}.",
                            _options.PublisherChannelTimeout
                        );
                        throw new TimeoutException("RabbitMQ publisher channel creation timed out.");
                    }
                }

                _publisherChannels = Channel.CreateBounded<IChannel>(
                    new BoundedChannelOptions(_options.PublisherChannelCount) { FullMode = BoundedChannelFullMode.Wait }
                );

                foreach (var channel in channels)
                {
                    if (!_publisherChannels.Writer.TryWrite(channel))
                    {
                        throw new InvalidOperationException("RabbitMQ publisher channel pool is full.");
                    }
                }
            }
            catch
            {
                foreach (var channel in channels)
                {
                    await channel.DisposeAsync();
                }

                throw;
            }
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task PublishAsync(ReadOnlyMemory<byte> rawBatch, CancellationToken ct)
    {
        var publisherChannels =
            _publisherChannels
            ?? throw new InvalidOperationException("RabbitMQ publisher channel pool is not initialized.");
        IChannel? channel = null;
        var canReturnChannel = false;

        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutSource.CancelAfter(_options.PublishConfirmationTimeout);

            try
            {
                // A RabbitMQ channel is not safe for concurrent publishing, so lease one exclusively.
                channel = await publisherChannels.Reader.ReadAsync(timeoutSource.Token);
                await channel.BasicPublishAsync(
                    exchange: string.Empty,
                    routingKey: _options.QueueName,
                    // Do not silently lose the batch if the target queue does not exist.
                    mandatory: true,
                    basicProperties: new BasicProperties
                    {
                        ContentType = "application/x-ndjson",
                        DeliveryMode = DeliveryModes.Persistent,
                    },
                    body: rawBatch,
                    cancellationToken: timeoutSource.Token
                );
                canReturnChannel = true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "RabbitMQ publish/confirmation timed out after {ConfiguredTimeout}.",
                    _options.PublishConfirmationTimeout
                );
                throw new TimeoutException("RabbitMQ publish/confirmation timed out.");
            }
        }
        finally
        {
            if (channel is not null && (!canReturnChannel || !publisherChannels.Writer.TryWrite(channel)))
            {
                await channel.DisposeAsync();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _initializationGate.WaitAsync();

        if (_isDisposed)
        {
            _initializationGate.Release();
            return;
        }

        try
        {
            _isDisposed = true;

            if (_publisherChannels is not null)
            {
                _publisherChannels.Writer.TryComplete();

                while (_publisherChannels.Reader.TryRead(out var channel))
                {
                    await channel.DisposeAsync();
                }

                _publisherChannels = null;
            }
        }
        finally
        {
            _initializationGate.Release();
        }
    }
}
