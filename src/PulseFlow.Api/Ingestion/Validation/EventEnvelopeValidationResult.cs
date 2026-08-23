using PulseFlow.Api.Ingestion.Contracts;

namespace PulseFlow.Api.Ingestion.Validation;

// These are contract errors, not HTTP response codes.
public enum EventEnvelopeValidationErrorCode
{
    RootNotObject,
    EventIdMissing,
    EventIdNotString,
    EventIdInvalidUuid,
    TypeMissing,
    TypeNull,
    TypeNotString,
    TypeEmpty,
    SourceMissing,
    SourceNull,
    SourceNotString,
    SourceEmpty,
    OccurredAtMissing,
    OccurredAtNotString,
    OccurredAtInvalidRfc3339,
    OccurredAtNotUtcZ,
    PayloadMissing,
    PayloadNull,
    PayloadNotObject,
}

public sealed class EventEnvelopeValidationError
{
    public EventEnvelopeValidationError(EventEnvelopeValidationErrorCode code, string jsonPath)
    {
        Code = code;
        JsonPath = jsonPath;
    }

    public EventEnvelopeValidationErrorCode Code { get; }

    public string JsonPath { get; }
}

public sealed class EventEnvelopeValidationResult
{
    private EventEnvelopeValidationResult(EventEnvelope? envelope, IReadOnlyList<EventEnvelopeValidationError> errors)
    {
        Envelope = envelope;
        Errors = errors;
    }

    public bool IsValid => Envelope is not null;

    public EventEnvelope? Envelope { get; }

    public IReadOnlyList<EventEnvelopeValidationError> Errors { get; }

    internal static EventEnvelopeValidationResult Valid(EventEnvelope envelope)
    {
        return new EventEnvelopeValidationResult(envelope, Array.Empty<EventEnvelopeValidationError>());
    }

    internal static EventEnvelopeValidationResult Invalid(IEnumerable<EventEnvelopeValidationError> errors)
    {
        // The result must not depend on the input error list.
        var retainedErrors = Array.AsReadOnly(errors.ToArray());

        if (retainedErrors.Count == 0)
        {
            throw new ArgumentException("An invalid result must contain an error.", nameof(errors));
        }

        return new EventEnvelopeValidationResult(null, retainedErrors);
    }
}
