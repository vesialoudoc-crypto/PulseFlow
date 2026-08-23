using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using PulseFlow.Api.Ingestion.Contracts;

namespace PulseFlow.Api.Ingestion.Validation;

// Validates parsed JSON against Event Contract v2.
public sealed class EventEnvelopeValidator
{
    // TryParse alone accepts formats outside RFC 3339.
    private static readonly Regex Rfc3339TimestampPattern = new(
        @"^\d{4}-(?:0[1-9]|1[0-2])-(?:0[1-9]|[12]\d|3[01])[Tt](?:[01]\d|2[0-3]):[0-5]\d:[0-5]\d(?:\.\d+)?(?:[Zz]|[+-](?:[01]\d|2[0-3]):[0-5]\d)$",
        RegexOptions.CultureInvariant
    );

    public EventEnvelopeValidationResult Validate(JsonElement record)
    {
        if (record.ValueKind != JsonValueKind.Object)
        {
            return EventEnvelopeValidationResult.Invalid([
                CreateError(EventEnvelopeValidationErrorCode.RootNotObject, "$"),
            ]);
        }

        // Collect all field errors for this record.
        var errors = new List<EventEnvelopeValidationError>();
        var eventId = ValidateEventId(record, errors);
        string? type = ValidateRequiredNonEmptyString(
            record,
            "type",
            EventEnvelopeValidationErrorCode.TypeMissing,
            EventEnvelopeValidationErrorCode.TypeNull,
            EventEnvelopeValidationErrorCode.TypeNotString,
            EventEnvelopeValidationErrorCode.TypeEmpty,
            errors
        );
        string? source = ValidateRequiredNonEmptyString(
            record,
            "source",
            EventEnvelopeValidationErrorCode.SourceMissing,
            EventEnvelopeValidationErrorCode.SourceNull,
            EventEnvelopeValidationErrorCode.SourceNotString,
            EventEnvelopeValidationErrorCode.SourceEmpty,
            errors
        );
        var occurredAt = ValidateOccurredAt(record, errors);
        var payload = ValidatePayload(record, errors);

        if (errors.Count > 0)
        {
            return EventEnvelopeValidationResult.Invalid(errors);
        }

        var envelope = new EventEnvelope(eventId!.Value, type!, source!, occurredAt!.Value, payload!.Value);

        return EventEnvelopeValidationResult.Valid(envelope);
    }

    private static Guid? ValidateEventId(JsonElement record, ICollection<EventEnvelopeValidationError> errors)
    {
        const string JsonPath = "$.eventId";

        if (!record.TryGetProperty("eventId", out var property))
        {
            errors.Add(CreateError(EventEnvelopeValidationErrorCode.EventIdMissing, JsonPath));
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            errors.Add(CreateError(EventEnvelopeValidationErrorCode.EventIdNotString, JsonPath));
            return null;
        }

        if (!Guid.TryParse(property.GetString(), out var eventId))
        {
            errors.Add(CreateError(EventEnvelopeValidationErrorCode.EventIdInvalidUuid, JsonPath));
            return null;
        }

        return eventId;
    }

    private static string? ValidateRequiredNonEmptyString(
        JsonElement record,
        string propertyName,
        EventEnvelopeValidationErrorCode missingCode,
        EventEnvelopeValidationErrorCode nullCode,
        EventEnvelopeValidationErrorCode notStringCode,
        EventEnvelopeValidationErrorCode emptyCode,
        ICollection<EventEnvelopeValidationError> errors
    )
    {
        string jsonPath = $"$.{propertyName}";

        if (!record.TryGetProperty(propertyName, out var property))
        {
            errors.Add(CreateError(missingCode, jsonPath));
            return null;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            errors.Add(CreateError(nullCode, jsonPath));
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            errors.Add(CreateError(notStringCode, jsonPath));
            return null;
        }

        string value = property.GetString()!;

        if (value.Length == 0)
        {
            errors.Add(CreateError(emptyCode, jsonPath));
            return null;
        }

        return value;
    }

    private static DateTime? ValidateOccurredAt(JsonElement record, ICollection<EventEnvelopeValidationError> errors)
    {
        const string JsonPath = "$.occurredAt";

        if (!record.TryGetProperty("occurredAt", out var property))
        {
            errors.Add(CreateError(EventEnvelopeValidationErrorCode.OccurredAtMissing, JsonPath));
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            errors.Add(CreateError(EventEnvelopeValidationErrorCode.OccurredAtNotString, JsonPath));
            return null;
        }

        string value = property.GetString()!;

        if (
            !Rfc3339TimestampPattern.IsMatch(value)
            || !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedTimestamp
            )
        )
        {
            errors.Add(CreateError(EventEnvelopeValidationErrorCode.OccurredAtInvalidRfc3339, JsonPath));
            return null;
        }

        if (!value.EndsWith('Z'))
        {
            // Event Contract v2 requires the UTC Z suffix.
            errors.Add(CreateError(EventEnvelopeValidationErrorCode.OccurredAtNotUtcZ, JsonPath));
            return null;
        }

        return parsedTimestamp.UtcDateTime;
    }

    private static JsonElement? ValidatePayload(JsonElement record, ICollection<EventEnvelopeValidationError> errors)
    {
        const string JsonPath = "$.payload";

        if (!record.TryGetProperty("payload", out var property))
        {
            errors.Add(CreateError(EventEnvelopeValidationErrorCode.PayloadMissing, JsonPath));
            return null;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            errors.Add(CreateError(EventEnvelopeValidationErrorCode.PayloadNull, JsonPath));
            return null;
        }

        if (property.ValueKind != JsonValueKind.Object)
        {
            errors.Add(CreateError(EventEnvelopeValidationErrorCode.PayloadNotObject, JsonPath));
            return null;
        }

        // Do not validate fields inside payload.
        return property;
    }

    private static EventEnvelopeValidationError CreateError(EventEnvelopeValidationErrorCode code, string jsonPath)
    {
        return new EventEnvelopeValidationError(code, jsonPath);
    }
}
