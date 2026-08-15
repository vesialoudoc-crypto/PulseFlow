# Roadmap

## Purpose

The roadmap defines the sequence of learning and implementation. It is not a promise of a specific architecture or timeline. Each stage must begin by clarifying requirements and end with a working, verifiable result.

## Statuses

- **Not started** — implementation of the stage has not begun.
- **In progress** — the current small outcome has been defined and work is underway.
- **Completed** — the expected result of the stage has been achieved and verified.
- **Deferred** — the stage has been deliberately postponed for a recorded reason.

A status applies to the stage as a whole. Incomplete details should be listed in `04_CURRENT_STATE.md` rather than hidden behind an overall completion percentage.

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
- Separate stable truth, the roadmap, and mutable current state.
- Create a reliable entry point for future ChatGPT/Codex sessions.

### Learning Objectives

- Learn to separate product requirements from architectural decisions.
- Define verifiable outcomes without premature design.

### Expected Result

The repository contains the linked documents `00_PROJECT_CONTEXT.md`, `01_SOURCE_OF_TRUTH.md`, `02_ROADMAP.md`, and `04_CURRENT_STATE.md`. They honestly state that no implementation exists yet and do not present proposed technologies as accepted decisions.

## Stage 1: Basic Data Ingestion

**Status:** Not started

### Goals

- Create a minimal ASP.NET Core API that accepts an event or a small batch.
- Define the first version of the contract, validation rules, and request result.
- Persist accepted data in PostgreSQL.
- Provide a minimal way to verify the persisted result.
- Prepare reproducible local execution and basic automated tests.

### Learning Objectives

- The ASP.NET Core request lifecycle and HTTP API design.
- Validation at the system boundary and error handling.
- Working with PostgreSQL, transactions, and migrations.
- Integration testing of the API and database.

### Expected Result

From a clean environment, one can start the application and PostgreSQL, submit a valid request, see the persisted result, and receive a predictable error for an invalid request. The first-version contract and constraints are documented.

### Do Not Decide in Advance

The queue, Redis, multiple application instances, the final event schema, or the cloud topology.

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

1. Update the stage status and actual result.
2. Put the precise implementation snapshot and open questions in `04_CURRENT_STATE.md`.
3. Record architectural decisions in ADRs if genuine alternatives and consequences existed.
4. Do not rewrite past goals as though deviations and experiments never occurred; explain useful changes in direction.
