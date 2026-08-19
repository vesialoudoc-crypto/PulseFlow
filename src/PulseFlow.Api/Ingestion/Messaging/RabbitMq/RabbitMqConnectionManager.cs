using RabbitMQ.Client;

namespace PulseFlow.Api.Ingestion.Messaging.RabbitMq;

internal sealed class RabbitMqConnectionManager : IAsyncDisposable
{
    private readonly ConnectionFactory _connectionFactory;
    private readonly RabbitMqOptions _options;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private IConnection? _connection;
    private bool _isInitialized;
    private bool _isDisposed;

    public RabbitMqConnectionManager(
        string connectionString,
        RabbitMqOptions options)
    {
        _connectionFactory = new ConnectionFactory
        {
            Uri = new Uri(connectionString)
        };
        _options = options;
    }

    internal IConnection? Connection => _connection;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // Messaging components may start together, but setup must happen once.
        await _initializationGate.WaitAsync(cancellationToken);

        try
        {
            ThrowIfDisposed();

            if (_isInitialized)
            {
                return;
            }

            _connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);

            // This short-lived channel only prepares the queue before workers start.
            await using var topologyChannel = await _connection.CreateChannelAsync(
                cancellationToken: cancellationToken);
            await topologyChannel.ExchangeDeclareAsync(
                exchange: _options.DeadLetterExchangeName,
                type: ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                arguments: null,
                passive: false,
                noWait: false,
                cancellationToken: cancellationToken);
            await topologyChannel.QueueDeclareAsync(
                queue: _options.DeadLetterQueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);
            await topologyChannel.QueueBindAsync(
                queue: _options.DeadLetterQueueName,
                exchange: _options.DeadLetterExchangeName,
                routingKey: _options.DeadLetterRoutingKey,
                arguments: null,
                cancellationToken: cancellationToken);
            await topologyChannel.QueueDeclareAsync(
                queue: _options.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                // Existing queues must be recreated manually before these arguments change.
                arguments: new Dictionary<string, object?>
                {
                    ["x-dead-letter-exchange"] = _options.DeadLetterExchangeName,
                    ["x-dead-letter-routing-key"] = _options.DeadLetterRoutingKey
                },
                cancellationToken: cancellationToken);

            _isInitialized = true;
        }
        catch
        {
            // Do not keep a half-initialized connection after startup fails.
            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            throw;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async ValueTask<IChannel> CreateChannelAsync(
        CreateChannelOptions? options,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);

        return await (_connection ?? throw new InvalidOperationException(
            "RabbitMQ connection is not initialized."))
            .CreateChannelAsync(options, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        // Shutdown uses the same gate, so it cannot close a connection during setup.
        await _initializationGate.WaitAsync();

        try
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
