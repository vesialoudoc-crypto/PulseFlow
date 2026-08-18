using PulseFlow.Api.Ingestion.Messaging;

namespace PulseFlow.UnitTests.Ingestion.Messaging;

public sealed class RabbitMqIngestionBatchPublisherTests
{
    [Fact]
    public async Task PublishAsync_RawBatchProvided_PassesItUnchangedToChannelProvider()
    {
        // Arrange
        var channelProvider = new RecordingPublisherChannelProvider();
        var publisher = new RabbitMqIngestionBatchPublisher(channelProvider);
        var expectedBatch = new byte[] { 1, 2, 3 };

        // Act
        await publisher.PublishAsync(
            expectedBatch,
            CancellationToken.None);

        // Assert
        Assert.Equal(expectedBatch, channelProvider.PublishedBatch);
    }

    #region Test helpers

    private sealed class RecordingPublisherChannelProvider :
        IRabbitMqPublisherChannelProvider
    {
        public byte[]? PublishedBatch { get; private set; }

        public Task PublishAsync(
            ReadOnlyMemory<byte> rawBatch,
            CancellationToken cancellationToken)
        {
            PublishedBatch = rawBatch.ToArray();

            return Task.CompletedTask;
        }
    }

    #endregion
}
