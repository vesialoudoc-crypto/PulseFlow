using RabbitMQ.Client;

namespace PulseFlow.Api.Ingestion.Messaging;

public sealed class RabbitMqPublisherChannelProvider :
    IRabbitMqPublisherChannelProvider,
    IAsyncDisposable
{
    private readonly ConnectionFactory _connectionFactory;
    private readonly RabbitMqOptions _options;
    private readonly SemaphoreSlim _channelGate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;
    private bool _disposed;

    public RabbitMqPublisherChannelProvider(
        string connectionString,
        RabbitMqOptions options)
    {
        _connectionFactory = new ConnectionFactory
        {
            Uri = new Uri(connectionString)
        };
        _options = options;
    }

    public async Task PublishAsync(
        ReadOnlyMemory<byte> rawBatch,
        CancellationToken cancellationToken)
    {
        await _channelGate.WaitAsync(cancellationToken);

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_channel is null)
            {
                await InitializeAsync(cancellationToken);
            }

            var channel = _channel
                ?? throw new InvalidOperationException(
                    "RabbitMQ channel initialization did not complete.");

            var properties = new BasicProperties
            {
                ContentType = "application/x-ndjson",
                DeliveryMode = DeliveryModes.Persistent
            };

            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: _options.QueueName,
                mandatory: true,
                basicProperties: properties,
                body: rawBatch,
                cancellationToken: cancellationToken);
        }
        finally
        {
            _channelGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _channelGate.WaitAsync();

        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var channel = _channel;
            var connection = _connection;
            _channel = null;
            _connection = null;

            try
            {
                if (channel is not null)
                {
                    await channel.DisposeAsync();
                }
            }
            finally
            {
                if (connection is not null)
                {
                    await connection.DisposeAsync();
                }
            }
        }
        finally
        {
            _channelGate.Release();
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        IConnection? connection = null;
        IChannel? channel = null;

        try
        {
            connection = await _connectionFactory.CreateConnectionAsync(
                cancellationToken);

            var channelOptions = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true);

            channel = await connection.CreateChannelAsync(
                channelOptions,
                cancellationToken);

            await channel.QueueDeclareAsync(
                queue: _options.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: cancellationToken);

            _connection = connection;
            _channel = channel;
            connection = null;
            channel = null;
        }
        finally
        {
            if (channel is not null)
            {
                await channel.DisposeAsync();
            }

            if (connection is not null)
            {
                await connection.DisposeAsync();
            }
        }
    }
}
