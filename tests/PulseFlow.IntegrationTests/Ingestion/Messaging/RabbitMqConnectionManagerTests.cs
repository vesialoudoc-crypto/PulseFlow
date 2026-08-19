using PulseFlow.Api.Ingestion.Messaging;
using PulseFlow.Api.Ingestion.Messaging.RabbitMq;

namespace PulseFlow.IntegrationTests.Ingestion.Messaging;

public sealed class RabbitMqConnectionManagerTests
{
    [Fact]
    public async Task InitializeAsync_AfterDisposal_ThrowsObjectDisposedException()
    {
        // Arrange
        var manager = new RabbitMqConnectionManager(
            "amqp://guest:guest@localhost:5672/",
            new RabbitMqOptions
            {
                QueueName = "pulseflow.connection-manager-test"
            });
        await manager.DisposeAsync();

        // Act
        var action = () => manager.InitializeAsync(CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(action);
    }
}
