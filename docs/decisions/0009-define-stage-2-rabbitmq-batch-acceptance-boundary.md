# ADR 0009: Define the Stage 2 RabbitMQ Batch Acceptance Boundary

**Date:** 2026-08-18

## Status

Accepted

## Context

[ADR 0008](0008-use-rabbitmq-to-decouple-http-ingestion-from-parsing.md) accepts
RabbitMQ as the Stage 2 broker between `PulseFlow.Api` and
`EventParserConsumer`. It deliberately left the initial RabbitMQ message
representation and the minimum HTTP acceptance/publishing contract unresolved.

The first Stage 2 implementation slice needs those two decisions before the current
synchronous HTTP path can be replaced. In Stage 1, the API parses NDJSON, validates
Event Contract v1, persists valid records, and then returns HTTP `200` accounting.
That accounting cannot describe record outcomes when parsing and validation happen
after the API response.

## Options

1. Publish the complete raw NDJSON batch body to RabbitMQ and confirm publication
   before acknowledging the HTTP request.
2. Store the raw batch externally and publish only a reference or identifier.
3. Parse individual events in the API and publish record-level messages.
4. Return HTTP `202 Accepted` after initiating publication without waiting for a
   RabbitMQ publication confirmation.

## Decision

For the first Stage 2 slice, `PulseFlow.Api` publishes the complete raw NDJSON batch
body as the RabbitMQ message. The API does not parse individual records or apply
Event Contract v1 validation before publishing. External raw-batch storage and
reference-only messages are not introduced in this slice.

RabbitMQ publication confirmation is the HTTP acceptance boundary:

```text
RabbitMQ publication confirmed
then
HTTP 202 Accepted
```

The API returns HTTP `202 Accepted` only after RabbitMQ confirms successful
publication of the batch. If publication fails, the API must not return `202
Accepted`. This ADR does not define a more specific failure response; existing
centralized error handling continues to apply unless a later implementation step
requires a narrower decision.

## Consequences

- The Stage 2 path is `PulseFlow.Api -> raw NDJSON batch -> RabbitMQ ->
  EventParserConsumer -> NDJSON parsing -> Event Contract v1 validation ->
  PostgreSQL`.
- `EventParserConsumer`, not the API, owns individual-record parsing and Event
  Contract v1 validation on the asynchronous path.
- HTTP `202 Accepted` means confirmed broker acceptance of the raw batch, not that
  any record has been parsed, validated, or persisted.
- The Stage 1 synchronous `200` response with `total`, `accepted`, and `rejected`
  is obsolete for the replacement asynchronous HTTP path. Step 2 must remove HTTP
  and integration tests that exist specifically to preserve that obsolete contract,
  rather than rewriting them to preserve obsolete semantics.
- Parsing, validation, persistence, and other tests that remain valid behind the
  RabbitMQ boundary remain useful and are not removed merely because execution moves.
- A full raw batch becomes the initial broker message unit, so request, batch, and
  message-size limits will need a later bounded decision.

## Explicit non-goals and unresolved questions

This ADR does not decide:

- RabbitMQ exchange/queue topology or routing-key conventions;
- acknowledgement/requeue semantics, retry policy, dead-letter queues, or delivery
  guarantees;
- Outbox, idempotency, deduplication, or client retry behavior;
- request, batch, and message-size limits, or compression;
- batch-status persistence, a public status/query endpoint, or the exact HTTP `202`
  response body;
- deployment topology for RabbitMQ or parser consumers.

These reliability and operational choices remain incremental design gates. This ADR
does not assert end-to-end delivery guarantees beyond the specified boundary that the
API must receive successful RabbitMQ publication confirmation before returning HTTP
`202 Accepted`.
