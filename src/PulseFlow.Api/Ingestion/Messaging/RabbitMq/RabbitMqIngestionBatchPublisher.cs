using RabbitMQ.Client;

namespace PulseFlow.Api.Ingestion.Messaging.RabbitMq;

internal sealed class RabbitMqIngestionBatchPublisher : IIngestionBatchPublisher, IAsyncDisposable
{
    private readonly RabbitMqConnectionManager _connectionManager;
    private readonly RabbitMqOptions _options;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private IChannel? _channel;
    private bool _isDisposed;

    internal IChannel? Channel => _channel;

    public RabbitMqIngestionBatchPublisher(RabbitMqConnectionManager connectionManager, RabbitMqOptions options)
    {
        _connectionManager = connectionManager;
        _options = options;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        if (_channel is not null)
        {
            return;
        }

        // Startup and the first HTTP request can race, but they use one channel.
        await _initializationGate.WaitAsync(ct);

        try
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);

            if (_channel is null)
            {
                var channelOptions = new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true
                );

                _channel = await _connectionManager.CreateChannelAsync(channelOptions, ct);
            }
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task PublishAsync(ReadOnlyMemory<byte> rawBatch, CancellationToken ct)
    {
        await InitializeAsync(ct);
        // Many requests share this channel, so publish one batch at a time.
        await _publishGate.WaitAsync(ct);

        try
        {
            if (_channel is null)
            {
                throw new InvalidOperationException("RabbitMQ publisher channel is not initialized.");
            }

            await _channel.BasicPublishAsync(
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
            _publishGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        _initializationGate.Dispose();
        _publishGate.Dispose();
    }
}
