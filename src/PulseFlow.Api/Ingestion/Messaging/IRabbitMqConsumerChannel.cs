namespace PulseFlow.Api.Ingestion.Messaging;

public interface IRabbitMqConsumerChannel : IAsyncDisposable
{
    Task StartConsumingAsync(
        Func<RabbitMqDelivery, CancellationToken, Task> deliveryHandler,
        CancellationToken cancellationToken);

    Task AcknowledgeAsync(
        ulong deliveryTag,
        CancellationToken cancellationToken);

    Task StopConsumingAsync(CancellationToken cancellationToken);
}
