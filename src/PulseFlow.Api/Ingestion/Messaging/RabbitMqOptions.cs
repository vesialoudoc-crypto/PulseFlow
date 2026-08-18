using System.ComponentModel.DataAnnotations;

namespace PulseFlow.Api.Ingestion.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required]
    public string QueueName { get; init; } = string.Empty;
}
