# ADR 0008: Use RabbitMQ to Decouple HTTP Ingestion from Parsing

**Date:** 2026-08-18

## Status

Accepted

## Context

Stage 1 is complete. Its implemented ingestion path receives an NDJSON request,
parses and validates every record, persists valid records to PostgreSQL, and returns
synchronous HTTP 200 accounting.

PulseFlow must also support many external clients submitting event and log batches.
Keeping NDJSON parsing and Event Contract v1 validation in HTTP request processing
couples API acknowledgement throughput to the throughput of record parsing,
validation, and persistence. That coupling prevents parsing capacity from being scaled
independently of the HTTP ingestion boundary.

Stage 2 needs a work-transfer broker between the API and the component that parses,
validates, and persists batches. This is an intentional change to the Stage 1
synchronous processing boundary, not a claim that the Stage 1 design was incorrect.

## Decision

RabbitMQ is the required Stage 2 message broker in the main ingestion path:

```text
many clients
    ↓
PulseFlow.Api
    ↓
RabbitMQ
    ↓
EventParserConsumer instances
    ↓
NDJSON parsing
    ↓
Event Contract v1 validation
    ↓
PostgreSQL
```

`PulseFlow.Api` remains the HTTP ingestion boundary. For the Stage 2 asynchronous
path, it receives an NDJSON batch, performs only request-level checks needed before
accepting it, publishes that work through RabbitMQ, and acknowledges asynchronous
acceptance. It must not parse individual NDJSON records or perform Event Contract v1
validation before publishing the batch.

`EventParserConsumer` is the concrete asynchronous component. It receives an NDJSON
batch from RabbitMQ, parses the batch records, applies the existing Event Contract v1
validation rules, and persists valid events to PostgreSQL through an appropriate
persistence boundary. Multiple instances must be possible so parsing and validation
capacity can scale independently of `PulseFlow.Api`.

The Stage 1 HTTP 200 response containing `total`, `accepted`, and `rejected` cannot
remain the final Stage 2 response contract unchanged, because record outcomes are no
longer available before HTTP acknowledgement. The future asynchronous endpoint is
expected to use semantics such as HTTP 202 Accepted, but its final response body is
not decided by this ADR.

## Why RabbitMQ is needed for this project

RabbitMQ is introduced to buffer accepted ingestion work, decouple HTTP ingestion
throughput from parsing and validation throughput, and distribute batches among parser
consumers. It enables the parser-consumer capacity to grow independently of the API
when many clients submit work. It is not a load balancer for HTTP requests and is not
included merely for portfolio breadth.

## Consequences

- Stage 2 changes the execution boundary from `HTTP -> parse -> validate ->
  PostgreSQL` to `HTTP -> RabbitMQ -> EventParserConsumer -> parse -> validate ->
  PostgreSQL`.
- `PulseFlow.Api` and parser consumers have distinct throughput responsibilities.
- The existing Stage 1 parsing, validation, envelope, and persistence boundaries are
  candidates for reuse or repositioning; the boundary move alone does not justify
  rewriting their rules.
- A successful HTTP acknowledgement no longer means that all individual records have
  been parsed, validated, or persisted.
- Stage 2 needs a later, bounded decision for the API's asynchronous acceptance
  contract and the minimum broker publishing contract before implementation changes
  the public endpoint.
- RabbitMQ is accepted as a future implementation requirement, but no RabbitMQ
  configuration, application code, consumer, or deployment exists yet.

## Explicit non-goals and unresolved questions

This ADR does not decide:

- whether RabbitMQ carries the complete raw NDJSON batch or a reference to separately
  stored raw data;
- message, batch, or request size limits, or compression;
- exchange/queue topology or routing-key conventions;
- acknowledgement/requeue semantics, retry policy, dead-letter queues, or exact
  delivery guarantees;
- Outbox, idempotency, deduplication, or Redis;
- batch-status persistence, a public status/query endpoint, or its response shape;
- deployment topology;
- the exact Stage 2 HTTP response body.

Those decisions remain incremental design gates. Reliability mechanisms and delivery
semantics belong primarily to Stage 3 unless an earlier implementation step makes a
smaller decision unavoidable.
