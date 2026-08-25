using System.Threading.Channels;
using RabbitMQ.Client;

namespace PulseFlow.Api.Ingestion.Messaging.RabbitMq;

internal sealed class RabbitMqIngestionBatchPublisher : IIngestionBatchPublisher, IAsyncDisposable
{
    private readonly Func<CreateChannelOptions, CancellationToken, ValueTask<IChannel>> _createChannelAsync;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqIngestionBatchPublisher> _logger;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private Channel<PublisherChannelSlot>? _publisherChannels;
    private int _usableChannelCount;
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

    public bool HasUsableChannel => Volatile.Read(ref _usableChannelCount) > 0;

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

            var channelOptions = CreatePublisherChannelOptions();
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

                _publisherChannels = Channel.CreateBounded<PublisherChannelSlot>(
                    new BoundedChannelOptions(_options.PublisherChannelCount) { FullMode = BoundedChannelFullMode.Wait }
                );

                foreach (var channel in channels)
                {
                    if (!_publisherChannels.Writer.TryWrite(new PublisherChannelSlot(channel)))
                    {
                        throw new InvalidOperationException("RabbitMQ publisher channel pool is full.");
                    }
                }

                Volatile.Write(ref _usableChannelCount, channels.Count);
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
        PublisherChannelSlot? slot = null;

        try
        {
            slot = await LeaseSlotAsync(publisherChannels, ct);
            await PublishWithConfirmationAsync(slot, rawBatch, ct);
        }
        finally
        {
            if (slot is not null)
            {
                if (!publisherChannels.Writer.TryWrite(slot))
                {
                    await DisposeSlotChannelAsync(slot);
                }
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

                while (_publisherChannels.Reader.TryRead(out var slot))
                {
                    await DisposeSlotChannelAsync(slot);
                }

                _publisherChannels = null;
            }
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async Task<PublisherChannelSlot> LeaseSlotAsync(
        Channel<PublisherChannelSlot> publisherChannels,
        CancellationToken ct
    )
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(_options.PublisherChannelTimeout);
        PublisherChannelSlot? slot = null;
        var slotLeased = false;

        try
        {
            // A RabbitMQ channel is not safe for concurrent publishing, so lease one slot exclusively.
            slot = await publisherChannels.Reader.ReadAsync(timeoutSource.Token);

            if (slot.Channel is null)
            {
                // This replacement serves only this future request; the prior batch is never retried.
                slot.Channel = await _createChannelAsync(CreatePublisherChannelOptions(), timeoutSource.Token);
                Interlocked.Increment(ref _usableChannelCount);
            }

            slotLeased = true;
            return slot;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            _logger.LogWarning(
                "RabbitMQ publisher channel wait or creation timed out after {ConfiguredTimeout}.",
                _options.PublisherChannelTimeout
            );
            throw new TimeoutException("RabbitMQ publisher channel wait or creation timed out.");
        }
        finally
        {
            // A failed replacement leaves the fixed slot available for a later request to recover.
            if (!slotLeased && slot is not null && !publisherChannels.Writer.TryWrite(slot))
            {
                await DisposeSlotChannelAsync(slot);
            }
        }
    }

    private async Task PublishWithConfirmationAsync(
        PublisherChannelSlot slot,
        ReadOnlyMemory<byte> rawBatch,
        CancellationToken ct
    )
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(_options.PublishConfirmationTimeout);

        try
        {
            await slot.Channel!.BasicPublishAsync(
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
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await DisposeSlotChannelAsync(slot);
            throw new OperationCanceledException(ct);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            await DisposeSlotChannelAsync(slot);
            _logger.LogWarning(
                "RabbitMQ publish/confirmation timed out after {ConfiguredTimeout}.",
                _options.PublishConfirmationTimeout
            );
            throw new TimeoutException("RabbitMQ publish/confirmation timed out.");
        }
        catch
        {
            await DisposeSlotChannelAsync(slot);
            throw;
        }
    }

    private static CreateChannelOptions CreatePublisherChannelOptions()
    {
        return new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true
        );
    }

    private async Task DisposeSlotChannelAsync(PublisherChannelSlot slot)
    {
        if (slot.Channel is not { } channel)
        {
            return;
        }

        slot.Channel = null;
        Interlocked.Decrement(ref _usableChannelCount);
        await channel.DisposeAsync();
    }

    private sealed class PublisherChannelSlot
    {
        public PublisherChannelSlot(IChannel? channel)
        {
            Channel = channel;
        }

        public IChannel? Channel { get; set; }
    }
}
