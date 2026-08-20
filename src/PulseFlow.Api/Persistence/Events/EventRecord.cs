namespace PulseFlow.Api.Persistence.Events;

public sealed class EventRecord
{
    public Guid Id { get; set; }

    public Guid EventId { get; set; }

    public string Type { get; set; } = null!;

    public string Source { get; set; } = null!;

    public DateTime OccurredAt { get; set; }

    public DateTime ReceivedAt { get; set; }

    public string PayloadJson { get; set; } = null!;
}
