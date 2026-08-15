# Project Context

## Purpose of This Document

This file is a concise entry point to the project for the author, ChatGPT, Codex, and new working sessions. It explains why the project exists, what it is intended to teach, and which constraints are important to preserve.

It is neither a technical specification nor a description of the current architecture. Specific decisions must be recorded separately and only after they have been accepted.

## Summary

The project is a platform for ingesting and processing data with ASP.NET Core. Its domain is intentionally small, but its technical depth is substantial.

External clients submit events, including batches of events. The system accepts and validates the data, coordinates its subsequent processing, and makes the result available for retrieval or verification. As the project evolves, it should progress from a simple working API to a deployed and observable system capable of operating reliably during failures and horizontal scaling.

The domain is intentionally narrow. The project's value comes not from the number of business entities or UI features, but from the quality of its backend engineering.

## Why the Project Exists

The project has two equally important goals:

1. Create a compelling portfolio project that can support discussions of engineering decisions, trade-offs, testing, and system operations.
2. Gain hands-on experience building a production-style backend system, adding complexity gradually and only when there is a clear reason for it.

The project should not pretend to be a finished commercial product. It is more important to show the development process honestly: initial requirements, accepted decisions, experiments, measurement results, and conclusions.

## Main Learning Areas

- ASP.NET Core and HTTP API internals;
- PostgreSQL, data design, and transactions;
- asynchronous processing;
- reliability, retries, duplicates, and idempotency;
- Redis and other supporting components, but only where their use is justified;
- concurrent access and parallel processing control;
- horizontal scaling and load testing;
- AWS and deployment infrastructure;
- CI/CD;
- logs, metrics, tracing, health checks, and operational diagnostics;
- security and production hardening.

The list above describes areas to study. It does not mean that every technology must become part of the final architecture.

## Project Principles

### Small Domain, Deep Engineering

New business capabilities are added only when they are needed for the engineering problem being studied. The project should not be inflated with analytics, a complex interface, or a large number of entities.

### A Working Vertical Slice First

Every stage should end with a verifiable result. A small system that works end to end is preferable to a large set of unfinished components.

### Complexity Must Be Earned

Queues, caches, additional data stores, and cloud services are not added merely to fill out a technology checklist. Before adding a component, document the problem, the alternatives, and the consequences of the decision.

### Decisions Are Made Incrementally

The queue technology, exact data schema, set of AWS services, deployment topology, and other details have not yet been determined. They are selected at the stage when sufficient requirements and evidence exist.

### Documentation Describes Reality

Documents must distinguish between:

- an established fact;
- a current assumption;
- a proposal for discussion;
- an accepted decision;
- a future possibility.

If the implementation diverges from the documentation, either update the document or explicitly record the unfinished work.

## Constraints

- The project is developed by one author as a learning and portfolio project.
- Infrastructure time and cost matter.
- Local dependencies and simplified deployment are acceptable in the early stages.
- A specific queue, specific AWS services, the exact number of instances, the final data schema, and operational targets must not be treated as selected in advance.
- Production-style means applying and explaining useful practices, not imitating the scale of a large company.

## What Constitutes a Good Outcome

A successful result is a repository in which one can:

- run and verify the complete data ingestion and processing path;
- see the evolution from a simple solution to one that is more reliable and scalable;
- understand the architecture and the reasons behind key decisions;
- reproduce tests and load experiments;
- observe system behavior and diagnose common failures;
- deploy the system to AWS through an automated change-delivery process;
- discuss limitations and next steps in concrete terms.

## Documentation Map

- [`01_SOURCE_OF_TRUTH.md`](01_SOURCE_OF_TRUTH.md) — stable product boundaries and mandatory properties.
- [`02_ROADMAP.md`](02_ROADMAP.md) — the sequence of stages, goals, and expected results.
- [`04_CURRENT_STATE.md`](04_CURRENT_STATE.md) — a current snapshot of completed work, open questions, and the next step.

Architecture documentation and ADRs should be created as actual decisions emerge. The currently absent `03_ARCHITECTURE.md` should not be filled with assumptions.

## Rules for New ChatGPT/Codex Sessions

Before planning or implementation, read this file, `01_SOURCE_OF_TRUTH.md`, and `04_CURRENT_STATE.md`. Also consult `02_ROADMAP.md` when selecting the next task.

When working on the project:

1. Do not present decisions that have not yet been accepted as existing architecture.
2. Do not add infrastructure or business features unless they support the goal of the current stage.
3. Propose a small, verifiable step and state assumptions explicitly.
4. For a significant choice, propose an ADR with the following sections: context, options, decision, and consequences.
5. After implementation, update `04_CURRENT_STATE.md` and the status of the corresponding stage.
6. If a proposal changes the stable product boundaries, discuss the change to `01_SOURCE_OF_TRUTH.md` first.

Example prompt for starting a new session:

> Read `docs/00_PROJECT_CONTEXT.md`, `docs/01_SOURCE_OF_TRUTH.md`, `docs/02_ROADMAP.md`, and `docs/04_CURRENT_STATE.md`. Treat only explicitly recorded decisions as accepted. Help complete the next small step of the current stage, and propose any necessary documentation updates at the end.
