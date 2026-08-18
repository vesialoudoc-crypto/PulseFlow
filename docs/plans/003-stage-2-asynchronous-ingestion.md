# PLAN 003: Stage 2 Asynchronous Ingestion

**Document type:** PLAN

**Status:** Not started

**ADR status:** This document is not an ADR. It sequences implementation of the
Stage 2 direction accepted in
[ADR 0008](../decisions/0008-use-rabbitmq-to-decouple-http-ingestion-from-parsing.md).
It preserves the implemented Stage 1 decisions and does not claim that RabbitMQ or an
asynchronous consumer currently exists.

## Purpose

Define a small, verifiable sequence that moves NDJSON parsing and Event Contract v1
validation behind an asynchronous RabbitMQ boundary. Stage 1 is complete: the API
currently parses NDJSON records, validates Event Contract v1, persists valid records
to PostgreSQL, and returns synchronous `{ total, accepted, rejected }` HTTP 200
accounting.

Stage 2 deliberately changes that boundary for the many-client ingestion scenario.
The target architecture is:

```text
Client
    ↓
PulseFlow.Api
    ↓
RabbitMQ
    ↓
EventParserConsumer
    ↓
NDJSON parsing
    ↓
Event Contract v1 validation
    ↓
PostgreSQL
```

`PulseFlow.Api` will accept a batch for asynchronous processing without parsing
individual records or applying Event Contract v1 validation. `EventParserConsumer`
will have the concrete role of receiving batches from RabbitMQ, parsing their NDJSON
records, applying Event Contract v1 validation, and persisting valid events to
PostgreSQL. The Stage 1 reader, validator, envelope, and persistence logic should be
reused or repositioned where suitable; changing the execution boundary is not a reason
to rewrite already proved rules.

## Planning constraints

- Keep the currently implemented Stage 1 architecture and its documentation as
  historical/current truth until a later step replaces the HTTP path.
- RabbitMQ buffers accepted ingestion work; it is not an HTTP load balancer and is not
  included merely as a portfolio technology.
- API acceptance and record-level processing are different boundaries. HTTP must not
  report Stage 1 record totals before asynchronous parsing occurs.
- The API performs only the request-level checks required before acceptance. It does
  not parse individual NDJSON records or perform Event Contract v1 validation before
  publishing a batch.
- `EventParserConsumer` must explicitly own asynchronous NDJSON parsing, Event
  Contract v1 validation, and persistence. Do not introduce an artificial generic
  business-processing worker or another service simply to increase service count.
- Prepare consumer ownership so multiple `EventParserConsumer` instances can consume
  from RabbitMQ independently of API instances. This plan proves structural scaling
  capability, not a Stage 4 load-performance claim.
- Do not choose reliability mechanisms early. Retries, dead-letter queues, delivery
  guarantees, Outbox, idempotency, and deduplication remain outside the first Stage 2
  slice unless a bounded preceding decision makes one unavoidable. Redis is deferred to
  Stage 4 for distributed ingestion rate limiting across multiple API instances.
- Use explicit constructors inside C# type bodies. Do not use C# primary constructors.

## Incremental implementation order

Each step is bounded. Its explicit non-goals prevent the plan from turning the first
asynchronous slice into speculative reliability or deployment work.

### Step 1: Decide the minimum asynchronous acceptance and publishing contract

**Implementation status:** Not started.

#### Responsibility introduced

Make only the decisions required to replace the Stage 1 synchronous HTTP boundary
safely: the minimum request-level checks before acceptance, the meaning of successful
RabbitMQ publication, the initial HTTP asynchronous-acceptance semantics, and the
batch representation passed to the broker. Record the decision and its consequences
before production code changes.

The decision must explicitly replace the Stage 1 assumption that the API can return
record-level `total`, `accepted`, and `rejected` accounting. It may select a minimal
HTTP 202 response only when the exact response body becomes necessary for the first
implementation slice.

#### What this step proves

- The API and consumer have a mutually understood, bounded work-transfer contract.
- The later API implementation can acknowledge asynchronous acceptance without
  pretending that record parsing or validation has finished.
- The plan has not silently selected a RabbitMQ topology or reliability model.

#### Explicit non-goals

- Do not implement an endpoint, RabbitMQ publisher, consumer, or persistence change.
- Do not decide message and batch limits, compression, topology, routing keys,
  acknowledgement/requeue semantics, retries, dead-letter queues, delivery
  guarantees, Outbox, idempotency, deduplication, Redis, or deployment topology.
- Do not create a batch-status schema or public status/query endpoint.

### Step 2: Add the API-to-RabbitMQ asynchronous acceptance slice

**Implementation status:** Not started.

#### Responsibility introduced

Introduce the minimum API composition needed to receive an NDJSON batch, apply the
Step 1 request-level checks, publish the agreed batch representation to RabbitMQ, and
return the agreed asynchronous-acceptance response. The endpoint no longer invokes
the Stage 1 reader, validator, handler, or PostgreSQL persistence path before its
acknowledgement.

#### What this step proves

- HTTP ingestion work is transferred to RabbitMQ before the API acknowledges it.
- API processing is independent of individual-record syntax and Event Contract v1
  validity.
- The public boundary no longer returns Stage 1 synchronous record accounting for the
  asynchronous path.

#### Explicit non-goals

- Do not parse NDJSON records, validate Event Contract v1, or persist events in the
  API request path.
- Do not introduce a generic worker, status endpoint, batch tracking schema, or
  business-processing stage.
- Do not add retry, requeue, DLQ, Outbox, idempotency, deduplication, Redis, or
  production deployment work.

### Step 3: Add `EventParserConsumer` using the existing parsing, validation, and persistence boundaries

**Implementation status:** Not started.

#### Responsibility introduced

Add `EventParserConsumer` as the concrete RabbitMQ consumer. It receives an accepted
batch, adapts it to the existing NDJSON reader where appropriate, applies the existing
Event Contract v1 validation rules, and persists valid events through an appropriate
persistence boundary to PostgreSQL. Reposition Stage 1 code only as required by the
new execution boundary; preserve its independently proved parsing, validation, and
persisted-event semantics.

#### What this step proves

- Record parsing and Event Contract v1 validation occur after RabbitMQ rather than in
  `PulseFlow.Api`.
- Valid events can reach PostgreSQL through the asynchronous consumer path.
- Existing Stage 1 parsing and validation rules are reused rather than duplicated or
  casually rewritten.

#### Explicit non-goals

- Do not invent a vague further-processing responsibility or add a separate
  artificial worker.
- Do not choose final per-batch result storage, a public query/status contract, or
  aggregate client-facing accounting.
- Do not add retry, requeue, dead-letter, Outbox, idempotency, deduplication, Redis,
  or delivery-guarantee mechanisms.

### Step 4: Prove the complete asynchronous path and independently configurable consumer capacity

**Implementation status:** Not started.

#### Responsibility introduced

Verify the actual `PulseFlow.Api -> RabbitMQ -> EventParserConsumer -> PostgreSQL`
path in a controlled integration scenario. Establish that consumer instances are
separately configurable from API instances and that more than one
`EventParserConsumer` can be introduced as competing consumers without moving parsing
or validation back into the HTTP boundary.

#### What this step proves

- An accepted HTTP batch reaches RabbitMQ and is parsed, validated, and persisted by
  `EventParserConsumer`.
- The API has acknowledged asynchronous acceptance before individual-event outcomes
  are known.
- Parser-consumer capacity is structurally independent from API capacity; this is not
  a throughput or load-test conclusion.

#### Explicit non-goals

- Do not claim a particular delivery guarantee, failure-recovery behavior, or
  horizontal-scaling performance result.
- Do not add retries, DLQ, Outbox, idempotency, deduplication, Redis, or load testing.
- Do not require a public batch-status endpoint solely to test the path; direct
  PostgreSQL verification remains acceptable where appropriate.

### Step 5: Review the Stage 2 slice and record the resulting boundary

**Implementation status:** Not started.

#### Responsibility introduced

Review the implemented slice against ADR 0008, Event Contract v1, the actual public
HTTP behavior, RabbitMQ composition, and the parser-consumer persistence path. Update
active architecture and roadmap documentation only to reflect proved implementation,
then create an immutable checkpoint.

#### What this step proves

- The repository accurately distinguishes the implemented Stage 2 path from deferred
  reliability, tracking, scaling-measurement, and deployment concerns.
- The required build, relevant automated tests, and documentation validation pass.
- The next decision is based on the real Stage 2 boundary rather than an assumed
  reliability design.

#### Explicit non-goals

- Do not expand the review into Stage 3 reliability work or Stage 4 load testing.
- Do not finalize delivery guarantees, retry/requeue behavior, DLQ, Outbox,
  idempotency, deduplication, Redis, or deployment topology merely to close the plan.
- Do not rewrite Stage 1 history or change `01_SOURCE_OF_TRUTH.md` without a genuine
  product-boundary change.

## Decisions intentionally deferred

The following are not selected by PLAN 003 and must be decided only when a bounded
implementation step makes them necessary:

- complete raw NDJSON batch versus separately stored-data reference as the RabbitMQ
  message representation;
- request, batch, and message-size limits; compression;
- exchange/queue topology and routing-key conventions;
- acknowledgement/requeue semantics, retries, dead-letter queues, and exact delivery
  guarantees;
- Outbox, idempotency, deduplication, and batch-status persistence; Redis rate-limiting
  implementation is deferred to Stage 4;
- public batch-status/query endpoint and response shape;
- deployment topology and performance targets.

## Completion criteria

PLAN 003 is complete only when:

1. The asynchronous acceptance and publishing contract has been explicitly decided.
2. The API publishes accepted batches to RabbitMQ without parsing individual NDJSON
   records or applying Event Contract v1 validation.
3. `EventParserConsumer` receives batches, performs asynchronous NDJSON parsing and
   Event Contract v1 validation, and persists valid events to PostgreSQL.
4. The complete path is verified, and parser-consumer capacity is structurally
   independent of API capacity.
5. Documentation accurately distinguishes the implemented Stage 2 path from deferred
   reliability and operational concerns.
6. Required repository verification commands succeed and a new checkpoint records the
   resulting state.
