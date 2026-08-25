using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PulseFlow.Api.Ingestion.Messaging.RabbitMq;

internal sealed class RabbitMqIngestionBatchConsumer : IIngestionBatchConsumer
{
    private readonly IChannel _channel;
    private readonly string _queueName;
    private readonly TaskCompletionSource _callbackFailure = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? _consumerTag;
    private bool _isDisposed;

    public RabbitMqIngestionBatchConsumer(IChannel channel, string queueName)
    {
        _channel = channel;
        _queueName = queueName;
    }

    public async Task ConsumeAsync(IngestionBatchHandler handler, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(handler);

        try
        {
            await StartAsync(handler, ct);
            await _callbackFailure.Task.WaitAsync(ct);
        }
        catch (Exception exception)
        {
            _started.TrySetException(exception);
            throw;
        }
    }

    public Task WaitUntilStartedAsync(CancellationToken cancellationToken)
    {
        return _started.Task.WaitAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        try
        {
            if (_consumerTag is not null)
            {
                await _channel.BasicCancelAsync(_consumerTag, noWait: false, CancellationToken.None);
                _consumerTag = null;
            }
        }
        finally
        {
            await _channel.DisposeAsync();
        }
    }

    private async Task StartAsync(IngestionBatchHandler handler, CancellationToken ct)
    {
        if (_consumerTag is not null)
        {
            return;
        }

        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += OnReceivedAsync;

        _consumerTag = await _channel.BasicConsumeAsync(
            queue: _queueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: ct
        );
        _started.TrySetResult();

        async Task OnReceivedAsync(object _, BasicDeliverEventArgs eventArgs)
        {
            // RabbitMQ owns this buffer after the callback ends, so copy it now.
            var delivery = CreateDelivery(eventArgs);

            try
            {
                // The application owns processing and settlement, not RabbitMQ primitives.
                await handler(delivery, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception exception)
            {
                _callbackFailure.TrySetException(exception);
                throw;
            }
        }
    }

    private IngestionBatchDelivery CreateDelivery(BasicDeliverEventArgs eventArgs)
    {
        Task AcknowledgeAsync(CancellationToken ct)
        {
            return _channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false, ct).AsTask();
        }

        Task RejectAsync(CancellationToken ct)
        {
            return _channel.BasicRejectAsync(eventArgs.DeliveryTag, requeue: false, ct).AsTask();
        }

        return new IngestionBatchDelivery(eventArgs.Body.ToArray(), AcknowledgeAsync, RejectAsync);
    }
}
