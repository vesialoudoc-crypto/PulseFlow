using System.Threading.Channels;
using RabbitMQ.Client;

namespace PulseFlow.Api.Ingestion.Messaging.RabbitMq;

internal sealed class RabbitMqIngestionBatchPublisher : IIngestionBatchPublisher, IAsyncDisposable
{
    private readonly Func<CreateChannelOptions, CancellationToken, ValueTask<IChannel>> _createChannelAsync;
    private readonly RabbitMqOptions _options;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private Channel<IChannel>? _publisherChannels;
    private bool _isDisposed;

    public RabbitMqIngestionBatchPublisher(RabbitMqConnectionManager connectionManager, RabbitMqOptions options)
        : this((channelOptions, ct) => connectionManager.CreateChannelAsync(channelOptions, ct), options) { }

    internal RabbitMqIngestionBatchPublisher(
        Func<CreateChannelOptions, CancellationToken, ValueTask<IChannel>> createChannelAsync,
        RabbitMqOptions options
    )
    {
        _createChannelAsync = createChannelAsync;
        _options = options;
    }

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
                    channels.Add(await _createChannelAsync(channelOptions, ct));
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
        // A RabbitMQ channel is not safe for concurrent publishing, so lease one exclusively.
        var channel = await publisherChannels.Reader.ReadAsync(ct);

        try
        {
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
                cancellationToken: ct
            );
        }
        finally
        {
            if (!publisherChannels.Writer.TryWrite(channel))
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
