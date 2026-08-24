# Roadmap

## Purpose

The roadmap defines the sequence of learning and implementation. It is not a promise of a specific architecture or timeline. Each stage must begin by clarifying requirements and end with a working, verifiable result.

## Statuses

- **Not started** — implementation of the stage has not begun.
- **In progress** — the current small outcome has been defined and work is underway.
- **Completed** — the expected result of the stage has been achieved and verified.
- **Deferred** — the stage has been deliberately postponed for a recorded reason.

A status applies to the stage as a whole. Incomplete details should be recorded in the most recent checkpoint in `docs/progress/` rather than hidden behind an overall completion percentage.

## Principles for Progressing Through the Stages

1. Build a minimal vertical slice first, then add infrastructure complexity.
2. Introduce a new component to solve an observed or explicitly stated problem.
3. Record options and criteria before a significant choice; create an ADR afterward when appropriate.
4. Every stage includes documentation, automated checks, and a demonstrable scenario.
5. The next stage does not require the previous one to be perfect, but it must not depend on unknown or non-working behavior.

## Stage 0: Documentation Foundation

**Status:** Completed (initial version)

### Goals

- Record the project's purpose, boundaries, and principles.
- Separate stable truth, the roadmap, and chronological progress records.
- Create a reliable entry point for future ChatGPT/Codex sessions.

### Learning Objectives

- Learn to separate product requirements from architectural decisions.
- Define verifiable outcomes without premature design.

### Expected Result

The repository contains the linked documents `00_PROJECT_CONTEXT.md`, `01_SOURCE_OF_TRUTH.md`, and `02_ROADMAP.md`, plus chronological checkpoint/handoff records in `docs/progress/`. The most recent checkpoint states what actually exists, what was verified, what remains unresolved, and what should happen next without presenting proposed technologies as accepted decisions.

## Stage 1: Basic Data Ingestion

**Status:** Completed

### Goals

- Create a minimal ASP.NET Core API for event ingestion using the accepted v1 event envelope.
- Define the remaining first-version HTTP contract, validation rules, and request result.
- Persist accepted data in PostgreSQL.
- Verify persistence with an integration test that may inspect PostgreSQL directly.
- Prepare reproducible local execution and basic automated tests.

### Learning Objectives

- The ASP.NET Core request lifecycle and HTTP API design.
- Validation at the system boundary and error handling.
- Working with PostgreSQL, transactions, and migrations.
- Integration testing of the API and database.

### Expected Result

From a clean environment, one can start the application and PostgreSQL, submit a valid ingestion request, have an integration test confirm the persisted data directly in PostgreSQL, and receive a predictable error for an invalid request. The first-version contract and constraints are documented. A public retrieval endpoint is not required to verify persistence for this stage.

### Current Scope Clarifications

- PulseFlow is a production-style event ingestion and processing system, not a CRUD API for events.
- The accepted Event Contract v1 envelope and its opaque arbitrary JSON object `payload` remain unchanged.
- A technical event identifier may exist in the persistence model, but it does not imply that `GET /api/events/{id}` is a primary user flow or a Stage 1 requirement.
- An integration test may verify successful ingestion by querying PostgreSQL directly; no public GET endpoint is required solely for persistence verification.
- A query or retrieval API for operators or data consumers has not been defined.
- The mechanism that transfers persisted events to later processing has not been defined.
- Batch ingestion uses NDJSON framing: each completely received line is an independent Event Contract v1 record. Valid records may be accepted independently; a malformed or truncated record does not invalidate other completely received valid records. See [ADR 0001](decisions/0001-use-ndjson-for-batch-ingestion.md).
- NDJSON records are read, parsed, and validated sequentially. Valid records are persisted to PostgreSQL in configurable chunks, and a record becomes accepted only when the transaction containing its chunk commits successfully. See [ADR 0002](decisions/0002-use-chunked-postgresql-persistence-for-ingestion.md).
- The chunk size is operational configuration rather than part of the public contract. The implemented initial value is 100, is configurable and validated, and is not a tuned performance conclusion; later load measurements may change it.
- On normal completion, ingestion orchestration reports total and accepted counts with rejected derived as `Total - Accepted`. A chunk-store failure propagates without a handler result; earlier committed chunks remain durable. The resulting retry/idempotency problem remains unresolved. See [ADR 0006](decisions/0006-keep-ingestion-handler-failure-propagation-simple.md).
- Normal partial-invalid HTTP 200 semantics are accepted in [ADR 0007](decisions/0007-expose-controller-based-ndjson-ingestion-api.md). Batch limits and compression have not been accepted.

### Do Not Decide in Advance

Do not treat the initial chunk capacity as tuned, or select RabbitMQ, polling, a queue or stream technology, delivery guarantees, idempotency semantics, an API query model, batch limits, compression, PostgreSQL retry behavior, transaction isolation level, multiple application instances, the final event schema, or the cloud topology before a separate decision establishes the need and criteria. Redis is retained for the accepted future distributed ingestion rate-limiting use case, but its algorithm, quota values, window strategy, implementation, and failure behavior remain undecided.

## Stage 2: Asynchronous Processing

**Status:** Completed

### Goals

- Decouple HTTP batch ingestion from NDJSON parsing and Event Contract v1 validation
  for the many-client ingestion scenario.
- Use RabbitMQ as the Stage 2 work-transfer broker between `PulseFlow.Api` and
  `EventParserConsumer` instances.
- Move NDJSON parsing, Event Contract v1 validation, and persistence behind the
  asynchronous boundary.
- Establish a minimal verifiable asynchronous path in which parser-consumer capacity
  can be increased independently from API capacity.

### Learning Objectives

- Boundaries between HTTP acceptance and asynchronous parsing, validation, and
  persistence.
- RabbitMQ work transfer and the limits of the selected initial semantics.
- Consumer ownership, independently scalable parser capacity, and graceful shutdown.
- Coordination between the API, broker, parser consumer, and PostgreSQL.

### Expected Result

`PulseFlow.Api` accepts an NDJSON batch without parsing individual records or applying
Event Contract v1 validation, publishes the complete raw batch body to RabbitMQ, and
returns HTTP `202 Accepted` only after RabbitMQ confirms successful publication.
`EventParserConsumer` receives the batch, performs NDJSON
parsing and Event Contract v1 validation, and persists valid events through an
appropriate boundary to PostgreSQL. Multiple parser consumers can be introduced to
increase parsing capacity independently of the API. The accepted RabbitMQ decision and
the limits of the initial semantics are documented. A public batch-status endpoint is
not required until its contract and storage model have been separately decided.

### Current implementation

PLAN 003 Steps 1 through 5 are implemented. `PulseFlow.Api` publishes the complete
raw NDJSON body and returns HTTP 202 only after RabbitMQ confirms publication.
`AddIngestionMessaging` hides RabbitMQ resource ownership from application composition.
It owns one application connection, one publisher-confirmation channel, and one
consumer channel for each worker. One hosted `EventParserConsumer` starts the validated
number of competing workers, reuses the Stage 1 parsing/validation/chunked-persistence
path, and acknowledges a delivery only after that handler succeeds. Real RabbitMQ and
PostgreSQL Testcontainers tests verify the complete path and, through RabbitMQ queue
metadata, that `ConsumerCount = 2` registers two consumers on the configured queue.
`RabbitMq:ConsumerCount` defaults to 1 and is validated as positive; it
controls parser-consumer capacity, not HTTP API instance count. Retry, dead-letter,
requeue, Outbox, idempotency, delivery guarantees, and batch status remain intentionally
unresolved. The adapter copies each delivery body and invokes the application handler
directly; it has no application-level queue or buffer. Broker-side flow control through
RabbitMQ prefetch is intentionally deferred.

### Do Not Decide in Advance

- Request and message-size limits, compression, exchange and queue topology,
  routing-key conventions, acknowledgement/requeue behavior, retry policy,
  dead-letter queues, delivery guarantees, broker-side flow control, RabbitMQ
  prefetch, and deployment topology. Prefetch must be designed with the outcome of a
  parsing or persistence failure, rather than selected before acknowledgement behavior
  for that failure is decided.
- Outbox, idempotency, deduplication, batch-status persistence, and public
  status/query endpoints; these are not prerequisites for the first Stage 2 slice.
  Redis implementation is deferred to Stage 4 for distributed ingestion rate limiting,
  not for generic caching or batch-status storage.

## Stage 3: Reliability and Correctness During Failures

**Status:** Completed

### Goals

- Define and implement behavior for repeated requests and repeated delivery.
- Decide what happens to a delivery when parsing or persistence fails before choosing
  RabbitMQ prefetch or other broker-side flow control.
- Define retry and recovery behavior, distinguishing recoverable and unrecoverable
  processing outcomes.
- Explicitly choose no automatic processing retry: a terminal processing failure is
  rejected without requeueing to the DLQ for later manual recovery.
- Test partial failures between the main components.
- Prevent silent data loss in the selected scenarios.

### Learning Objectives

- Idempotency and deduplication.
- Transaction boundaries and consistency.
- At-most-once, at-least-once, and the consequences of the chosen model.
- Backoff, retries, poison messages, and failure recovery.
- Races, locks, and concurrent updates.

### Expected Result

Automated or reproducible checks exist for a documented set of failures and repetitions. The system reaches a defined state, does not produce inexplicable results, and provides enough information for diagnosis. Accepted guarantees are stated without claiming "exactly once" unless that claim has been demonstrated within defined boundaries.

### Current implementation

The first reliability slice is implemented. For an unexpected parsing or PostgreSQL
persistence failure, `EventParserConsumer` logs the failure, rejects only that RabbitMQ
delivery with `requeue = false`, and routes it to a durable dedicated dead-letter queue.
Each delivery receives exactly one complete processing attempt. Successful processing
still acknowledges only after the handler completes; shutdown cancellation does not
reject a delivery. Malformed NDJSON and contract-invalid records remain record-level
outcomes inside the established pipeline and do not dead-letter the whole batch.

The main queue uses application-owned dead-letter queue arguments. An existing local
queue that was declared before this change must be recreated manually because RabbitMQ
does not permit those queue arguments to change. Application startup never deletes,
purges, or silently recreates a queue. The implementation and its limitations are
recorded in [ADR 0012](decisions/0012-process-each-rabbitmq-delivery-once.md).

The hosted parser consumer starts exactly the configured number of workers and passes
the host stopping token to each one. It waits for all worker tasks with
`Task.WhenAll`; it does not coordinate worker failures, cancel siblings, restart
workers, or treat a normally completed worker as a special failure. A worker exception
naturally faults the `BackgroundService` after `Task.WhenAll` completes. This lifecycle
is recorded in [ADR 0013](decisions/0013-use-backgroundservice-worker-lifecycle.md).

The next reliability slice is implemented through Event Contract v2. Every valid event
has a source-owned UUID `eventId`; the logical identity is `(source, eventId)`. The
database stores non-null `event_id` and enforces a unique `(source, event_id)` index.
Each persistence chunk remains one explicit transaction and one PostgreSQL
`INSERT ... ON CONFLICT (source, event_id) DO NOTHING` command. A valid duplicate is a
successful no-op, including when a complete batch is replayed after a later chunk
failed. Missing or malformed event IDs remain record-level invalid outcomes and do not
dead-letter a whole batch. The real-PostgreSQL tests cover duplicate replay, different
sources with one UUID, partial chunk replay, and concurrent inserts. See
[ADR 0011](decisions/0011-use-event-level-idempotency.md).

This is not an end-to-end no-loss, at-least-once, or exactly-once guarantee. RabbitMQ
dead-letter republishing can fail, and earlier PostgreSQL chunks can already be durable
when a later chunk fails. No automatic retry or DLQ redrive exists; later recovery is
manual and separately defined. Retry queues, Outbox, and prefetch remain unresolved.

## Stage 4: Horizontal Scaling and Load

**Status:** In progress

### Goals

- Run multiple instances of applicable components.
- Verify distribution and concurrent-processing correctness.
- Introduce Redis-backed distributed ingestion rate limiting so multiple
  `PulseFlow.Api` instances share one global quota state.
- Reject an over-limit request with HTTP 429, with `Retry-After` where appropriate,
  before it is published to RabbitMQ.
- Create a reproducible load scenario.
- Find and measure at least one real bottleneck.
- Compare behavior before and after a justified improvement.

### Learning Objectives

- Stateless design and coordination through external state.
- Distributed quota counters and rate limiting across independently serving API
  instances.
- Parallelism limits, backpressure, and resource saturation.
- Throughput, latency, errors, and percentile interpretation.
- Connection pools, indexes, locks, and the database's impact on scaling.

### Expected Result

The load test runs reproducibly and produces a clear report. The configuration, baseline metrics, identified constraint, change, and follow-up measurement are documented. Multiple `PulseFlow.Api` instances share Redis-backed rate-limit/quota state: process-local in-memory counters are not used because they would be incorrect when requests are distributed across instances. Redis stores this fast-changing operational state only; it is not the primary event store, generic cache, or batch-status store. Requests exceeding the accepted quota receive HTTP 429 and are not published to RabbitMQ. Multiple instances work correctly in the tested scenarios, and the limits of the conclusions are stated explicitly.

### Current implementation

The Stage 4 Redis limiter foundation is implemented. `StackExchange.Redis` is
registered as one shared process-level `IConnectionMultiplexer`, configured through
`ConnectionStrings:Redis`. `IngestionRateLimit:RequestLimit` and
`IngestionRateLimit:WindowDuration` are startup-validated. The registered
`RedisIngestionRateLimiter` implements the Redis-independent
`IIngestionRateLimiter` contract with one atomic Lua script and the global key
`pulseflow:rate-limit:ingestion:global`. It uses the accepted fixed-window algorithm,
returns an exceeded result with the Redis TTL as `RetryAfter`, and fails closed with an
unavailable result for a Redis operation failure. The accepted global scope and
fail-closed behavior are recorded in
[ADR 0014](decisions/0014-use-redis-for-global-ingestion-rate-limiting.md).

`EventsController` checks the limiter before it reads the request body or calls the
RabbitMQ publisher. An exceeded result returns HTTP 429 with `Retry-After` rounded up
to whole delta seconds; an unavailable result returns HTTP 503. Focused HTTP tests
verify these mappings, confirm that rejected requests do not call the publisher, and
confirm that an allowed request is published and returns HTTP 202.

The reproducible load-test harness is available at
[`tests/performance/ingestion-baseline.js`](../tests/performance/ingestion-baseline.js). It uses a closed model
with configurable VUs and duration, posts one valid Event Contract v2 NDJSON record
per request, and checks for HTTP 202. It intentionally has no performance thresholds
or measured target. [Ingestion Performance Baseline 001](performance/ingestion-baseline-001.md)
is retained unchanged as the historical pre-fix three-level local sweep at commit
`ad1da229afeb36dbdf17e7a18d780196a137abd6`. It predates the RabbitMQ
publisher-channel pool and performance-runner correctness changes; its 30-VU result
therefore does not represent the current configuration.

[Ingestion Performance Baseline 002](performance/ingestion-baseline-002.md) records
the final fresh post-fix `10 VU / 10s`, `20 VU / 10s`, and `30 VU / 10s` sweep for
one API instance, `RabbitMq:ConsumerCount = 1`, `RabbitMq:PublisherChannelCount = 4`,
and `Ingestion:ChunkCapacity = 100`. The corrected runner verifies completion by
requiring PostgreSQL's persisted event count to equal the k6 HTTP-202 acceptance
count after the queue is empty. Every Baseline 002 row meets that invariant. Its
local-run instructions require a high enough local rate-limit quota to prevent HTTP
429 from becoming the limiting factor. Stage 4 remains in progress, and no
bottleneck conclusion, target, or optimization decision has been made.

### Do Not Decide in Advance

The exact number of instances and target performance metrics remain undecided. This
first local baseline alone is not sufficient evidence to select either. Expanded load
scenarios and measured quota values remain Stage 4 work.

## Stage 5: Deployment to AWS

**Status:** Not started

### Goals

- Select a minimal AWS architecture based on the already working system.
- Automate the creation or configuration of the required infrastructure.
- Configure secure storage for configuration and secrets.
- Create a CI/CD path with automated checks and controlled deployment.
- Limit costs and document how to remove or stop resources.

### Learning Objectives

- Networking, compute, managed services, and the AWS responsibility model.
- Infrastructure as Code.
- Building, testing, packaging, and delivering the application.
- Configuration, secret, and environment management.
- Basic cloud security and cost-control practices.

### Expected Result

The system can be deployed reproducibly to AWS, a demonstration scenario can be performed, and a verified change can be delivered through an automated process. The architecture, operational commands, approximate cost, and safe resource-removal procedure are documented.

### Do Not Decide in Advance

Specific AWS services, the number of environments, network topology, or release strategy before the stage requirements and costs have been assessed.

## Stage 6: Production Hardening

**Status:** Not started

### Goals

- Collect logs, metrics, and traces around the critical path.
- Define health/readiness checks and minimal operational signals.
- Harden the public surface and supply chain.
- Verify backup and recovery where applicable.
- Prepare a runbook, final architecture, and portfolio demonstration.

### Learning Objectives

- Diagnosing distributed requests and building useful signals.
- Alerting, operational thresholds, and noise reduction.
- Authentication, authorization, rate limiting, and dependency management.
- Graceful shutdown, updates, rollbacks, and recovery.
- Defining SLI/SLO targets based on measurements.

### Expected Result

The demonstration includes the normal flow and several controlled failures that are visible in system signals and can be investigated using the runbook. Automated checks, documentation, and the final README allow another developer to evaluate the system, reproduce key scenarios, and understand its limitations.

## Deferred Portfolio and Production Coverage

The following are intentional future learning and portfolio concerns, not rejected
requirements and not claims about the currently implemented architecture:

- liveness and readiness health checks;
- structured logging and correlation IDs;
- metrics, distributed tracing, and broader observability;
- authentication and authorization;
- rate limiting and request/input limits;
- resilience and retry policies where concrete failure behavior justifies them;
- Redis-backed distributed ingestion rate limiting when Stage 4 introduces multiple
  `PulseFlow.Api` instances; Redis is not planned as generic caching or batch-status
  storage;
- asynchronous/background processing and messaging when Stage 2 requires them;
- load testing and multi-instance behavior;
- graceful shutdown and operational behavior;
- CI/CD, AWS deployment, and production configuration and secrets.

Specific technologies and guarantees remain just-in-time decisions for the stages
that establish their requirements and verification criteria.

## Updating the Roadmap

After completing a meaningful step:

1. Update the stage status or expected result only when it actually changed.
2. Create a new checkpoint in `docs/progress/` with the starting point, changes, resulting state, verification results, decisions, unresolved items, and next recommended step.
3. Record architectural decisions in ADRs if genuine alternatives and consequences existed.
4. Do not rewrite earlier checkpoints except to correct factual errors; use a new checkpoint to explain useful changes in direction.
