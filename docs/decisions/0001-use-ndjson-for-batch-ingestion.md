# ADR 0001: Use NDJSON for Batch Ingestion

**Date:** 2026-08-15

## Status

Accepted

## Context

PulseFlow is a production-style event ingestion and processing system. One target scenario is a client that may remain offline for an extended period and later upload a potentially large accumulation of diagnostic events.

An upload may be interrupted before it completes, and an individual event record may be malformed. The business or diagnostic value of completely received, valid events must not be lost merely because another record in the same upload is malformed or because the connection is interrupted while a later record is being sent.

Each event continues to use [Event Contract v1](../contracts/event-ingestion-v1.md): `type`, `source`, `occurredAt`, and an opaque JSON-object `payload`. PulseFlow must not require sender-specific payload models in order to frame or recover valid records.

The transport format therefore needs a clear recovery boundary that allows records to be parsed and considered independently without guessing missing client intent.

## Considered Options

### 1. One JSON array containing all events

Advantages:

- It is a familiar standard JSON representation.
- Many HTTP clients and serializers support arrays directly.
- A complete, valid upload has one simple document-level schema.

Disadvantages:

- If the upload is truncated, the array is not a complete JSON document.
- A malformed element can prevent reliable parsing of the remaining document.
- Although an array can be parsed incrementally, recovering independent records after syntax damage is difficult because the element boundary and the enclosing document structure are coupled.
- Document-level validity encourages all-or-nothing handling even when earlier events were completely received and valid.

### 2. One JSON array with heuristic repair of malformed or truncated JSON

Advantages:

- In narrowly defined cases, a server might salvage data from an otherwise invalid array.
- Clients could continue sending a familiar array representation.

Disadvantages:

- The server cannot reliably infer missing braces, values, separators, or record boundaries.
- A repair may create data the client never sent or change the meaning of the upload.
- Heuristics make correctness, security review, testing, and operational diagnosis substantially harder.
- Different failure shapes would require expanding a non-deterministic repair policy over time.
- Recoverability would depend on guessing client intent rather than on an explicit transport boundary.

### 3. NDJSON with one independent Event Contract v1 record per line

Advantages:

- Each completely received line provides an explicit record boundary.
- A malformed record can be rejected without invalidating other valid records.
- If an upload is interrupted during the last record, earlier completely received records remain parseable.
- The server can parse the upload as a stream without representing the complete batch as one JSON document object model.
- Record-level partial acceptance follows naturally from the framing.
- Event Contract v1 remains unchanged because NDJSON defines batch transport, not event-envelope semantics.

Disadvantages:

- The server must validate and account for results at record granularity.
- Clients must understand that an upload can be partially accepted.
- HTTP response semantics are more complex than a single all-or-nothing result.
- Limits and protection are needed for oversized records and uploads.
- A retry after an uncertain connection outcome can resend records that the server already accepted.

## Decision

PulseFlow batch ingestion uses NDJSON. Each NDJSON record contains one complete Event Contract v1 JSON object.

The semantic boundary is:

> A completely received record is an independent unit of ingestion.

Completely received and valid records may be accepted independently of other records in the same upload. A malformed record does not automatically reject other valid records. If an upload is interrupted during its last record, earlier completely received records remain eligible for processing.

An incomplete or malformed record is not repaired heuristically. The server must not add brackets, reconstruct fields, or substitute assumed values. Recoverability comes from NDJSON record framing, not from guessing the sender's intent.

This decision does not select a PostgreSQL transaction boundary or require a particular persistence implementation. Independent ingestion semantics and physical transaction strategy are separate concerns.

## Consequences

Positive consequences:

- Damage to one record does not destroy the complete batch.
- Earlier records remain parseable when an upload ends during a later record.
- Streaming parsing is possible without holding the upload as one JSON document object model.
- Partial acceptance can be represented naturally at record granularity.
- Event Contract v1 and its opaque payload semantics remain unchanged.

Negative consequences and new responsibilities:

- The server needs record-level validation and result accounting.
- The client needs to understand partial acceptance.
- If the connection ends after some records were accepted but before the client receives the response, retrying the upload can create duplicates.
- Idempotency and deduplication must therefore be considered separately during the reliability stage; this ADR does not select their semantics.
- Limits and protection against oversized records and uploads must be defined separately.

## Deferred Decisions

This ADR does not decide:

- the maximum size of one record;
- the maximum upload size or record count;
- compression;
- the HTTP route;
- success, partial-success, or error response formats and status codes;
- authentication or authorization;
- idempotency, deduplication, or client retry policy;
- RabbitMQ, Redis, polling, or any queue or stream technology;
- PostgreSQL transaction strategy or persistence implementation.
