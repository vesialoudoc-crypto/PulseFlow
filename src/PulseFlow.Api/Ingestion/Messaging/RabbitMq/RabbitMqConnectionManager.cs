using RabbitMQ.Client;

namespace PulseFlow.Api.Ingestion.Messaging.RabbitMq;

internal sealed class RabbitMqConnectionManager : IAsyncDisposable
{
    private readonly ConnectionFactory _connectionFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnectionManager> _logger;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private IConnection? _connection;
    private bool _isInitialized;
    private bool _isDisposed;

    public RabbitMqConnectionManager(
        string connectionString,
        RabbitMqOptions options,
        ILogger<RabbitMqConnectionManager> logger
    )
    {
        _connectionFactory = new ConnectionFactory
        {
            Uri = new Uri(connectionString),
            RequestedConnectionTimeout = options.ConnectionTimeout,
            HandshakeContinuationTimeout = options.HandshakeTimeout,
            ContinuationTimeout = options.ContinuationTimeout,
        };
        _options = options;
        _logger = logger;
    }

    public bool IsConnected => _connection?.IsOpen == true;

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

            using var topologyTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
            topologyTimeoutSource.CancelAfter(_options.TopologyDeclarationTimeout);

            try
            {
                // This short-lived channel is used only to declare RabbitMQ topology at startup.
                await using var topologyChannel = await _connection.CreateChannelAsync(
                    cancellationToken: topologyTimeoutSource.Token
                );

                await DeclareDeadLetterTopologyAsync(topologyChannel, topologyTimeoutSource.Token);
                await DeclareMainQueueAsync(topologyChannel, topologyTimeoutSource.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            catch (OperationCanceledException) when (topologyTimeoutSource.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "RabbitMQ topology declaration timed out after {ConfiguredTimeout}.",
                    _options.TopologyDeclarationTimeout
                );
                throw new TimeoutException("RabbitMQ topology declaration timed out.");
            }

            _isInitialized = true;
        }
        catch
        {
            CleanupFailedInitialization();
            throw;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async ValueTask<IChannel> CreateChannelAsync(CreateChannelOptions? options, CancellationToken ct)
    {
        ThrowIfDisposed();

        if (!_isInitialized || _connection is null)
        {
            throw new InvalidOperationException("RabbitMQ connection is not initialized.");
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

    private async Task DeclareDeadLetterTopologyAsync(IChannel channel, CancellationToken ct)
    {
        await DeclareDeadLetterExchangeAsync(channel, ct);
        await DeclareDeadLetterQueueAsync(channel, ct);
        await BindDeadLetterQueueAsync(channel, ct);
    }

    private Task DeclareDeadLetterExchangeAsync(IChannel channel, CancellationToken ct)
    {
        return channel.ExchangeDeclareAsync(
            exchange: _options.DeadLetterExchangeName,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            passive: false,
            noWait: false,
            cancellationToken: ct
        );
    }

    private Task DeclareDeadLetterQueueAsync(IChannel channel, CancellationToken ct)
    {
        return channel.QueueDeclareAsync(
            queue: _options.DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct
        );
    }

    private Task BindDeadLetterQueueAsync(IChannel channel, CancellationToken ct)
    {
        return channel.QueueBindAsync(
            queue: _options.DeadLetterQueueName,
            exchange: _options.DeadLetterExchangeName,
            routingKey: _options.DeadLetterRoutingKey,
            arguments: null,
            cancellationToken: ct
        );
    }

    private Task DeclareMainQueueAsync(IChannel topologyChannel, CancellationToken ct)
    {
        // Existing queues must be recreated manually if these declaration arguments change.
        // RabbitMQ uses these standard queue arguments to route rejected messages to the DLX.
        var arguments = new Dictionary<string, object?>
        {
            ["x-dead-letter-exchange"] = _options.DeadLetterExchangeName,
            ["x-dead-letter-routing-key"] = _options.DeadLetterRoutingKey,
        };

        return topologyChannel.QueueDeclareAsync(
            queue: _options.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: arguments,
            cancellationToken: ct
        );
    }

    private void CleanupFailedInitialization()
    {
        var connection = _connection;
        _connection = null;

        if (connection is null)
        {
            return;
        }

        _ = DisposeConnectionBestEffortAsync(connection);
    }

    private async Task DisposeConnectionBestEffortAsync(IConnection connection)
    {
        Task disposeTask;

        try
        {
            disposeTask = connection.DisposeAsync().AsTask();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "RabbitMQ connection cleanup could not be started.");
            return;
        }

        using var timeoutSource = new CancellationTokenSource(_options.CleanupTimeout);

        try
        {
            await disposeTask.WaitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            _logger.LogWarning(
                "RabbitMQ connection cleanup exceeded its best-effort timeout of {ConfiguredTimeout}.",
                _options.CleanupTimeout
            );
            await ObserveLateCleanupAsync(disposeTask);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "RabbitMQ connection cleanup failed.");
        }
    }

    private async Task ObserveLateCleanupAsync(Task disposeTask)
    {
        try
        {
            await disposeTask;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "RabbitMQ connection cleanup failed after its best-effort timeout.");
        }
    }
}
