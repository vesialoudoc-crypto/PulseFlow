using System.ComponentModel.DataAnnotations;

namespace PulseFlow.Api.Ingestion.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    // CancellationTokenSource.CancelAfter and the RabbitMQ.Client timer-backed timeouts
    // are kept within the project's safe timer deadline range.
    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

    [Required]
    public string QueueName { get; init; } = string.Empty;

    public string DeadLetterExchangeName => $"{QueueName}.dead-letter-exchange";

    public string DeadLetterQueueName => $"{QueueName}.dead-letter";

    public string DeadLetterRoutingKey => $"{QueueName}.dead-letter";

    [Range(1, int.MaxValue)]
    // This changes worker count in one process, not the number of API instances.
    public int ConsumerCount { get; init; } = 1;

    [Range(1, int.MaxValue)]
    // This bounds concurrent publishing in one API process.
    public int PublisherChannelCount { get; init; } = 4;

    public TimeSpan ConnectionTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan HandshakeTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan ContinuationTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan TopologyDeclarationTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan PublisherChannelTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan PublishConfirmationTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
