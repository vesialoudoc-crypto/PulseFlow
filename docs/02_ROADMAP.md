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

**Status:** In progress

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
- The chunk size is operational configuration rather than part of the public contract; its value has not been selected and must later be evaluated through load measurements.
- On normal completion, ingestion orchestration reports only accepted and rejected totals. A chunk-store failure propagates without a handler result; earlier committed chunks remain durable. The resulting retry/idempotency problem and HTTP failure behavior remain unresolved. See [ADR 0006](decisions/0006-keep-ingestion-handler-failure-propagation-simple.md).
- Batch limits, compression, and HTTP partial-success semantics have not been accepted.

### Do Not Decide in Advance

Do not select a concrete chunk size, RabbitMQ, Redis, polling, a queue or stream technology, delivery guarantees, idempotency semantics, an API query model, batch limits, compression, partial-success HTTP semantics, PostgreSQL retry behavior, transaction isolation level, multiple application instances, the final event schema, or the cloud topology before a separate decision establishes the need and criteria.

## Stage 2: Asynchronous Processing

**Status:** Not started

### Goals

- Separate data ingestion from at least one processing step.
- Define the lifecycle of an accepted event and its available states.
- Choose a work-transfer mechanism using explicit criteria.
- Implement background processing and a way to observe its result.

### Learning Objectives

- Boundaries between synchronous and asynchronous work.
- Message-delivery models and their trade-offs.
- Background processes, parallelism control, and graceful shutdown.
- Coordination of state between storage and the processor.

### Expected Result

Within the chosen semantics, the API acknowledges ingestion independently of how long subsequent processing takes. A processor performs the work and saves the result, and the event state can be inspected. The choice of work-transfer mechanism and its limitations are documented.

### Do Not Decide in Advance

A specific queue technology before requirements for local development, delivery, cost, AWS deployment, and resilience have been defined.

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

## Updating the Roadmap

After completing a meaningful step:

1. Update the stage status or expected result only when it actually changed.
2. Create a new checkpoint in `docs/progress/` with the starting point, changes, resulting state, verification results, decisions, unresolved items, and next recommended step.
3. Record architectural decisions in ADRs if genuine alternatives and consequences existed.
4. Do not rewrite earlier checkpoints except to correct factual errors; use a new checkpoint to explain useful changes in direction.
