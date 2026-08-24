using System.ComponentModel.DataAnnotations;

namespace PulseFlow.Api.Ingestion.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

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
}
