using System.Text.Json;

namespace PulseFlow.Api.Ingestion.Contracts;

// Created only after Event Contract v2 validation.
public sealed class EventEnvelope
{
    public Guid EventId { get; }

    public string Type { get; }

    public string Source { get; }

    public DateTime OccurredAt { get; }

    public JsonElement Payload { get; }

    internal EventEnvelope(Guid eventId, string type, string source, DateTime occurredAt, JsonElement payload)
    {
        if (string.IsNullOrEmpty(type))
        {
            throw new ArgumentException("Event type must be non-empty.", nameof(type));
        }

        if (string.IsNullOrEmpty(source))
        {
            throw new ArgumentException("Event source must be non-empty.", nameof(source));
        }

        if (occurredAt.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Occurrence time must be UTC.", nameof(occurredAt));
        }

        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Event payload must be a JSON object.", nameof(payload));
        }

        EventId = eventId;
        Type = type;
        Source = source;
        OccurredAt = occurredAt;

        // Keep the payload valid after the input JsonDocument is disposed.
        Payload = payload.Clone();
    }
}
