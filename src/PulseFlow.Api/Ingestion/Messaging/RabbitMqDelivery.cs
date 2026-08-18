namespace PulseFlow.Api.Ingestion.Messaging;

public sealed class RabbitMqDelivery
{
    public RabbitMqDelivery(
        ulong deliveryTag,
        ReadOnlyMemory<byte> body)
    {
        DeliveryTag = deliveryTag;
        Body = body;
    }

    public ulong DeliveryTag { get; }

    public ReadOnlyMemory<byte> Body { get; }
}
