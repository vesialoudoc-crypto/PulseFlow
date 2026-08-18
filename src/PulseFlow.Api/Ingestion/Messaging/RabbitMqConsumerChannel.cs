using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PulseFlow.Api.Ingestion.Messaging;

public sealed class RabbitMqConsumerChannel : IRabbitMqConsumerChannel
{
    private readonly IConnection _connection;
    private readonly RabbitMqOptions _options;
    private IChannel? _channel;
    private string? _consumerTag;

    public RabbitMqConsumerChannel(
        IConnection connection,
        RabbitMqOptions options)
    {
        _connection = connection;
        _options = options;
    }

    public async Task StartConsumingAsync(
        Func<RabbitMqDelivery, CancellationToken, Task> deliveryHandler,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deliveryHandler);

        if (_channel is not null)
        {
            throw new InvalidOperationException("RabbitMQ consumer channel has already started.");
        }

        // The publisher and consumer need separate RabbitMQ channels.
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            // RabbitMQ may reuse this buffer after the callback ends.
            var delivery = new RabbitMqDelivery(
                eventArgs.DeliveryTag,
                eventArgs.Body.ToArray());

            await deliveryHandler(delivery, cancellationToken);
        };

        _consumerTag = await _channel.BasicConsumeAsync(
            queue: _options.QueueName,
            // A successful handler decides when this message is acknowledged.
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);
    }

    public Task AcknowledgeAsync(
        ulong deliveryTag,
        CancellationToken cancellationToken)
    {
        var channel = _channel
            ?? throw new InvalidOperationException("RabbitMQ consumer channel has not started.");

        return channel.BasicAckAsync(
            deliveryTag,
            multiple: false,
            cancellationToken).AsTask();
    }

    public async Task StopConsumingAsync(CancellationToken cancellationToken)
    {
        if (_channel is null || _consumerTag is null)
        {
            return;
        }

        // Wait for RabbitMQ to confirm that this consumer has stopped.
        await _channel.BasicCancelAsync(
            _consumerTag,
            noWait: false,
            cancellationToken);

        _consumerTag = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }
    }
}
