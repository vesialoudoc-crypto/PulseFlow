using RabbitMQ.Client;
using PulseFlow.Api.Ingestion.Messaging.RabbitMq;
using PulseFlow.IntegrationTests.Infrastructure;

namespace PulseFlow.IntegrationTests.Ingestion.Messaging;

public sealed class RabbitMqIngestionBatchConsumerFlowControlTests : IClassFixture<RabbitMqFixture>
{
    private readonly RabbitMqFixture _rabbitMqFixture;

    public RabbitMqIngestionBatchConsumerFlowControlTests(RabbitMqFixture rabbitMqFixture)
    {
        _rabbitMqFixture = rabbitMqFixture;
    }

    [Fact]
    public async Task ReadAllAsync_FirstDeliveryIsUnacknowledged_LeavesNextDeliveryOnBroker()
    {
        // Arrange
        var queueName = $"pulseflow.flow-control.{Guid.NewGuid():N}";
        var connectionFactory = new ConnectionFactory
        {
            Uri = new Uri(_rabbitMqFixture.ConnectionString)
        };
        await using var connection = await connectionFactory.CreateConnectionAsync();
        await using var consumerChannel = await connection.CreateChannelAsync();
        await using var publisherChannel = await connection.CreateChannelAsync();
        await using var inspectionChannel = await connection.CreateChannelAsync();
        await consumerChannel.QueueDeclareAsync(
            queue: queueName,
            durable: false,
            exclusive: false,
            autoDelete: false);
        var consumer = new RabbitMqIngestionBatchConsumer(consumerChannel, queueName);
        await using var deliveries = consumer
            .ReadAllAsync(CancellationToken.None)
            .GetAsyncEnumerator();

        try
        {
            var firstDelivery = deliveries.MoveNextAsync().AsTask();
            await PublishAsync(publisherChannel, queueName, "first");
            await PublishAsync(publisherChannel, queueName, "second");

            // Act
            var receivedFirstDelivery = await firstDelivery.WaitAsync(TimeSpan.FromSeconds(5));
            var queuedMessageCount = await inspectionChannel.MessageCountAsync(queueName);

            // Assert
            Assert.True(receivedFirstDelivery);
            Assert.Equal(1u, queuedMessageCount);
        }
        finally
        {
            await consumer.DisposeAsync();
            await inspectionChannel.QueueDeleteAsync(queueName);
        }
    }

    #region Test helpers

    private static ValueTask PublishAsync(IChannel channel, string queueName, string body)
    {
        return channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: queueName,
            mandatory: false,
            body: System.Text.Encoding.UTF8.GetBytes(body));
    }

    #endregion
}
