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

    public async Task InitializeAsync(CancellationToken ct)
    {
        // Messaging components may start together, but setup must happen once.
        await _initializationGate.WaitAsync(ct);

        try
        {
            ThrowIfDisposed();

            if (_isInitialized)
            {
                return;
            }

            _connection = await _connectionFactory.CreateConnectionAsync(ct);

            // This short-lived channel is used only to declare RabbitMQ topology at startup.
            await using var topologyChannel = await _connection.CreateChannelAsync(
                cancellationToken: ct);

            await DeclareDeadLetterTopologyAsync(topologyChannel, ct);
            await DeclareMainQueueAsync(topologyChannel, ct);

            _isInitialized = true;
        }
        catch
        {
            await CleanupFailedInitializationAsync();
            throw;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async ValueTask<IChannel> CreateChannelAsync(
        CreateChannelOptions? options,
        CancellationToken ct)
    {
        await InitializeAsync(ct);

        if(_connection is null) 
        {
            throw new InvalidOperationException(
            "RabbitMQ connection is not initialized.");
        }

        return await _connection.CreateChannelAsync(options, ct);
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

    private async Task DeclareDeadLetterTopologyAsync(
        IChannel topologyChannel,
        CancellationToken ct)
    {
        // Rejected batches are routed through this exchange into the dead-letter queue.
        await topologyChannel.ExchangeDeclareAsync(
            exchange: _options.DeadLetterExchangeName,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            passive: false,
            noWait: false,
            cancellationToken: ct);
        await topologyChannel.QueueDeclareAsync(
            queue: _options.DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct);
        await topologyChannel.QueueBindAsync(
            queue: _options.DeadLetterQueueName,
            exchange: _options.DeadLetterExchangeName,
            routingKey: _options.DeadLetterRoutingKey,
            arguments: null,
            cancellationToken: ct);
    }

    private Task DeclareMainQueueAsync(
        IChannel topologyChannel,
        CancellationToken ct)
    {
        // Existing queues must be recreated manually if these declaration arguments change.
        // RabbitMQ uses these standard queue arguments to route rejected messages to the DLX.
        var arguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = _options.DeadLetterExchangeName,
            ["x-dead-letter-routing-key"] = _options.DeadLetterRoutingKey
        };

        return topologyChannel.QueueDeclareAsync(
            queue: _options.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: arguments,
            cancellationToken: ct);
    }

    private async Task CleanupFailedInitializationAsync()
    {
        if (_connection is null)
        {
            return;
        }

        await _connection.DisposeAsync();
        _connection = null;
    }
}
