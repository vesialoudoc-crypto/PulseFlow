using System.Threading.Channels;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PulseFlow.Api.Ingestion.Messaging.RabbitMq;

internal sealed class RabbitMqIngestionBatchConsumer : IIngestionBatchConsumer
{
    private const ushort PrefetchCount = 1;
    private readonly IChannel _channel;
    private readonly string _queueName;
    // One worker holds at most one copied batch before processing it.
    private readonly Channel<IngestionBatchDelivery> _deliveries =
        System.Threading.Channels.Channel.CreateBounded<IngestionBatchDelivery>(
            new BoundedChannelOptions(PrefetchCount)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true
            });
    private string? _consumerTag;
    private bool _isDisposed;

    public RabbitMqIngestionBatchConsumer(IChannel channel, string queueName)
    {
        _channel = channel;
        _queueName = queueName;
    }

    internal IChannel Channel => _channel;

    public async IAsyncEnumerable<IngestionBatchDelivery> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await StartAsync(cancellationToken);

        await foreach (var delivery in _deliveries.Reader.ReadAllAsync(cancellationToken))
        {
            yield return delivery;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        // Stop new deliveries before closing the broker channel.
        _deliveries.Writer.TryComplete();

        if (_consumerTag is not null)
        {
            await _channel.BasicCancelAsync(
                _consumerTag,
                noWait: false,
                CancellationToken.None);
            _consumerTag = null;
        }

        await _channel.DisposeAsync();
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_consumerTag is not null)
        {
            return;
        }

        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, eventArgs) =>
        {
            // RabbitMQ owns this buffer after the callback ends, so copy it now.
            var delivery = new IngestionBatchDelivery(
                eventArgs.Body.ToArray(),
                acknowledgementCancellationToken => _channel.BasicAckAsync(
                    eventArgs.DeliveryTag,
                    multiple: false,
                    acknowledgementCancellationToken).AsTask());

            // The app decides when to acknowledge after it processes this delivery.
            _deliveries.Writer.TryWrite(delivery);
            return Task.CompletedTask;
        };

        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: PrefetchCount,
            global: false,
            cancellationToken: cancellationToken);

        _consumerTag = await _channel.BasicConsumeAsync(
            queue: _queueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);
    }
}
