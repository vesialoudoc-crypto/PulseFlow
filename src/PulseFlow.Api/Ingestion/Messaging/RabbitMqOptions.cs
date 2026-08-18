using System.ComponentModel.DataAnnotations;

namespace PulseFlow.Api.Ingestion.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required]
    public string QueueName { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    // This changes parser capacity inside one API process, not the number of API instances.
    public int ConsumerCount { get; init; } = 1;
}
