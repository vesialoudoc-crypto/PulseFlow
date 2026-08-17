# ADR 0003: Construct EventEnvelope After Contract Validation

**Date:** 2026-08-16

## Status

Accepted

## Context

Event Contract v1 defines four required properties and their accepted values: non-empty
string `type` and `source` values, an RFC 3339 UTC `occurredAt` string serialized with
`Z`, and an opaque JSON-object `payload`.

Input at the ingestion boundary is untrusted. A syntactically valid JSON value can
still omit required properties, contain JSON `null`, use incorrect JSON types, or
contain a timestamp that does not satisfy the contract. If that input is deserialized
or bound directly into an `EventEnvelope` with nullable or otherwise weak fields, the
type can exist while holding a state that Event Contract v1 does not permit.

JSON syntax parsing and Event Contract validation answer different questions and need
an explicit boundary between them. The design also needs to avoid serializing and
parsing an already parsed record again merely to validate it.

## Considered Options

### 1. Deserialize directly into a nullable or weak EventEnvelope and validate it afterwards

Advantages:

- Uses familiar serializer or model-binding behavior.
- Requires fewer explicit boundary types and construction steps.

Disadvantages:

- `EventEnvelope` can exist with missing, null, incorrectly typed, empty, or otherwise
  contract-invalid fields.
- Downstream code cannot rely on the type to mean a valid Event Contract v1 event.
- Serializer behavior can blur the distinction between malformed JSON and valid JSON
  that violates the contract.
- Validation may require weak property types or other compromises in the envelope.

### 2. Keep an untrusted parsed JSON representation at the boundary, validate it, then construct EventEnvelope

Advantages:

- The untrusted representation preserves the distinctions among missing properties,
  JSON `null`, incorrect JSON types, and invalid values.
- `EventEnvelope` can represent only a valid Event Contract v1 event.
- Syntax failures and contract failures remain separate result categories.
- Validation can inspect the already-parsed JSON representation without a second JSON
  parse.

Disadvantages:

- Introduces an explicit boundary and validation result model.
- Requires more explicit validation and construction code than direct model binding.

## Decision

Accept option 2.

`EventEnvelope` represents only a valid Event Contract v1 event. Untrusted JSON must
not be deserialized or bound directly into an `EventEnvelope` whose fields can contain
invalid contract state.

The boundary has three distinct stages:

1. JSON parsing answers whether a record is syntactically valid JSON.
2. Contract validation answers whether that valid JSON satisfies Event Contract v1.
3. `EventEnvelope` exists only after both conditions are satisfied.

Each record's JSON text is parsed only once into an untrusted JSON representation,
such as `JsonElement`. Contract validation inspects that already-parsed representation;
it must not serialize and parse the JSON again. Only successful validation constructs
an `EventEnvelope`.

Any JSON retained by the constructed envelope must be owned independently of the
caller's parser-document lifetime. The `payload` remains opaque, and `EventEnvelope`
remains separate from the persistence-only `EventRecord`.

This decision does not determine how unknown top-level properties are handled.

## Consequences

Positive consequences:

- `EventEnvelope` has meaningful invariants.
- Downstream code does not repeatedly check required contract fields.
- Malformed JSON and contract-invalid JSON remain distinct failure categories.
- No second JSON parse is required.
- `EventEnvelope` remains separate from `EventRecord`.

Negative consequences:

- The design introduces an explicit boundary and result model.
- Validation and construction code is slightly more explicit than direct model binding.

