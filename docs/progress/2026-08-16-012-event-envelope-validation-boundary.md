# Checkpoint: EventEnvelope validation boundary implemented

**Date:** 2026-08-16

## Starting point

[PLAN 001](../plans/001-stage-1-persistence-foundation.md) was complete and its
persistence-only `EventRecord`, EF Core mapping, migration, and real-PostgreSQL tests
were verified. [PLAN 002](../plans/002-stage-1-ingestion-pipeline.md) was proposed but
none of its ingestion steps had been implemented.

Event Contract v1, ADR 0001, and ADR 0002 established the record fields, NDJSON batch
transport, independent record handling, configurable chunked persistence, and the
PostgreSQL commit acceptance boundary. They did not establish whether untrusted JSON
could first become a weak `EventEnvelope` and then be validated or whether validation
must precede envelope construction. PLAN 002's original target diagram placed
`EventEnvelope` before record validation.

This milestone implements only Step 1 of PLAN 002.

## What changed

- Added [ADR 0003](../decisions/0003-construct-event-envelope-after-contract-validation.md),
  accepting an untrusted parsed JSON boundary followed by Event Contract v1 validation
  and construction of `EventEnvelope` only after validation succeeds.
- Updated PLAN 002 and the accepted ingestion architecture to distinguish JSON syntax
  parsing, untrusted parsed JSON, Event Contract validation, and `EventEnvelope`.
- Added `EventEnvelope` under `PulseFlow.Api.Ingestion.Contracts` with immutable
  `Type`, `Source`, `OccurredAt`, and `Payload` properties.
- Added `EventEnvelopeValidator`, which accepts an already-parsed `JsonElement`,
  validates the Event Contract v1 record-level rules, and never serializes or parses
  the record again.
- Added the EventEnvelope-specific `EventEnvelopeValidationResult`,
  `EventEnvelopeValidationError`, and `EventEnvelopeValidationErrorCode` types. No
  generic validation or result abstraction was introduced.
- Cloned the validated object-valued payload while constructing `EventEnvelope`, so
  retained payload JSON remains usable after the caller disposes its `JsonDocument`.
- Added the API project reference required by `PulseFlow.UnitTests` and 41 focused
  unit cases for valid envelopes and all requested invalid categories.

## Resulting repository state

The implemented boundary is:

```text
already-parsed untrusted JsonElement
    ↓
EventEnvelopeValidator
    ↓
EventEnvelopeValidationResult
    ├─ valid: EventEnvelope + no errors
    └─ invalid: no EventEnvelope + one or more coded errors
```

`EventEnvelopeValidationResult` has exactly these public members:

- `bool IsValid`, true only when an envelope exists;
- `EventEnvelope? Envelope`, populated only for valid contract data;
- `IReadOnlyList<EventEnvelopeValidationError> Errors`, empty for a valid result and
  containing one or more errors for an invalid result.

Each error exposes `EventEnvelopeValidationErrorCode Code` and `string JsonPath`.
Codes distinguish a non-object root; missing, null, non-string, and empty `type` and
`source` values; missing or non-string `occurredAt`; invalid RFC 3339 timestamps;
timestamps not expressed with uppercase `Z`; and missing, null, or non-object
`payload` values. When an object has multiple invalid fields, the validator reports
all field errors it discovers in contract-field order.

A valid `EventEnvelope` contains only non-empty `Type` and `Source` strings, a parsed
UTC `DateTime` occurrence value, and an object-valued cloned `JsonElement` payload.
The arbitrary nested payload is not interpreted. `EventEnvelope` remains separate
from the unchanged persistence-only `EventRecord`.

The broader PLAN 002 flow is not implemented. There is no NDJSON reader, stream or
HTTP request handling, malformed-JSON result, ingestion handler, chunk store, chunk
formation, application database access, DI registration, or endpoint in this
milestone. Stage 1 therefore remains **In progress**, and the roadmap expected result
has not changed. Event Contract v1 remains marked unimplemented because no ingestion
HTTP surface exists.

## Verification

Required commands run from the repository root:

```powershell
dotnet build PulseFlow.slnx
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- `dotnet build PulseFlow.slnx` succeeds with 0 warnings and 0 errors.
- `dotnet test PulseFlow.slnx` succeeds: 41 unit tests and 3 integration tests passed,
  with 0 failed and 0 skipped. The integration run used the existing real PostgreSQL
  Testcontainers fixture and required access to the local Docker engine.
- `pwsh ./scripts/check-project-docs.ps1` completes without warnings.

## Decisions made

- Accepted [ADR 0003](../decisions/0003-construct-event-envelope-after-contract-validation.md):
  JSON syntax parsing and contract validation are separate, JSON is parsed only once,
  validation inspects the untrusted parsed representation, and only successful
  validation constructs `EventEnvelope`.
- Used a small EventEnvelope-specific validation result with coded errors and JSON
  paths. Returning all discovered contract-field errors is a bounded Step 1 behavior,
  not a generic validation framework or a public HTTP error contract.
- No additional architectural decision became unavoidable during implementation.
  The chosen type layout and timestamp-validation mechanics implement ADR 0003 and
  Event Contract v1 without selecting later ingestion behavior.

## Intentionally unresolved

- NDJSON framing, stream reading, per-record JSON syntax parsing, and malformed-record
  results assigned to PLAN 002 Step 2.
- LF and CRLF handling, blank-line behavior, clean final records without a trailing
  newline, and cancellation behavior required by Step 2.
- Contract behavior for unknown top-level properties. The Step 1 validator does not
  interpret them, and no test establishes their acceptance or rejection as a stable
  requirement.
- Duplicate top-level property behavior, maximum field lengths, record size, upload
  limits, and payload nesting limits.
- Identifier generation, receipt-time assignment, mapping to `EventRecord`,
  `IEventChunkStore`, chunk formation, orchestration, application database wiring, and
  the remaining HTTP contract.
- Retry, idempotency, deduplication, messaging, compression, authentication, and other
  concerns explicitly deferred by PLAN 002 and the roadmap.

## Next recommended step

Implement PLAN 002 Step 2 as a separate bounded milestone. Before implementation,
define the minimal NDJSON framing behavior required by that step. Parse each record
once into an untrusted JSON representation and preserve JSON syntax failures as
record-level results; reuse the Step 1 validator to construct `EventEnvelope` only
after contract validation succeeds. Do not introduce orchestration, chunking,
persistence, DI, or endpoint behavior in that step.
