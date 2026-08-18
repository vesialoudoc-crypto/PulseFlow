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

Do not treat the initial chunk capacity as tuned, or select RabbitMQ, Redis, polling, a queue or stream technology, delivery guarantees, idempotency semantics, an API query model, batch limits, compression, PostgreSQL retry behavior, transaction isolation level, multiple application instances, the final event schema, or the cloud topology before a separate decision establishes the need and criteria.

## Stage 2: Asynchronous Processing

**Status:** Not started

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
Event Contract v1 validation, publishes accepted work to RabbitMQ, and acknowledges
asynchronous acceptance. `EventParserConsumer` receives the batch, performs NDJSON
parsing and Event Contract v1 validation, and persists valid events through an
appropriate boundary to PostgreSQL. Multiple parser consumers can be introduced to
increase parsing capacity independently of the API. The accepted RabbitMQ decision and
the limits of the initial semantics are documented. A public batch-status endpoint is
not required until its contract and storage model have been separately decided.

### Do Not Decide in Advance

- The Stage 2 RabbitMQ message shape: a complete raw NDJSON batch versus a reference
  to separately stored raw data.
- Request and message-size limits, compression, exchange and queue topology,
  routing-key conventions, acknowledgement/requeue behavior, retry policy,
  dead-letter queues, delivery guarantees, and deployment topology.
- Outbox, idempotency, deduplication, Redis, batch-status persistence, and public
  status/query endpoints; these are not prerequisites for the first Stage 2 slice.

## Stage 3: Reliability and Correctness During Failures

**Status:** Not started

### Goals

- Define and implement behavior for repeated requests and repeated delivery.
- Introduce bounded retries and handling for unrecoverable messages.
- Test partial failures between the main components.
- Prevent silent data loss in the selected scenarios.
- Evaluate the need for Redis or another supporting mechanism against a concrete problem.

### Learning Objectives

- Idempotency and deduplication.
- Transaction boundaries and consistency.
- At-most-once, at-least-once, and the consequences of the chosen model.
- Backoff, retries, poison messages, and failure recovery.
- Races, locks, and concurrent updates.

### Expected Result

Automated or reproducible checks exist for a documented set of failures and repetitions. The system reaches a defined state, does not produce inexplicable results, and provides enough information for diagnosis. Accepted guarantees are stated without claiming "exactly once" unless that claim has been demonstrated within defined boundaries.

## Stage 4: Horizontal Scaling and Load

**Status:** Not started

### Goals

- Run multiple instances of applicable components.
- Verify distribution and concurrent-processing correctness.
- Create a reproducible load scenario.
- Find and measure at least one real bottleneck.
- Compare behavior before and after a justified improvement.

### Learning Objectives

- Stateless design and coordination through external state.
- Parallelism limits, backpressure, and resource saturation.
- Throughput, latency, errors, and percentile interpretation.
- Connection pools, indexes, locks, and the database's impact on scaling.

### Expected Result

The load test runs reproducibly and produces a clear report. The configuration, baseline metrics, identified constraint, change, and follow-up measurement are documented. Multiple instances work correctly in the tested scenarios, and the limits of the conclusions are stated explicitly.

### Do Not Decide in Advance

The exact number of instances or target performance metrics before a baseline measurement exists.

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
- idempotency and deduplication;
- caching or Redis only when a concrete read/query use case justifies caching;
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
