# Current State

**Current as of:** August 15, 2026  
**Overall status:** concept selected, documentation foundation created, implementation not started  
**Current stage:** preparing for Stage 1 — basic data ingestion

## Snapshot

The project is at its starting point. The concept of a narrow data ingestion platform built with ASP.NET Core has been defined, and the first version of the documents used to transfer context between working sessions has been created.

There is currently no application code, infrastructure, database schema, or accepted architectural decision. Any description of these at this stage is a proposal, not the current implementation.

## What Exists

- The concept has been selected: ingestion and processing of events from external clients.
- The goal has been established: a portfolio/learning project focused on production-style backend engineering.
- The principle of "small domain, deep engineering" has been established.
- The main learning areas have been defined: ASP.NET Core, PostgreSQL, asynchronous processing, reliability, concurrency, scaling, AWS, CI/CD, and observability.
- An initial documentation system has been created:
  - `00_PROJECT_CONTEXT.md`;
  - `01_SOURCE_OF_TRUTH.md`;
  - `02_ROADMAP.md`;
  - `04_CURRENT_STATE.md`.
- It has been established that Redis, a queue, and specific AWS services should be used only after they are justified.

## What Does Not Yet Exist

- a solution/repository structure for the application;
- ASP.NET Core source code;
- tests;
- an API contract;
- an event format and validation rules;
- a PostgreSQL schema and migrations;
- an asynchronous path and processor;
- a local execution environment;
- CI/CD;
- AWS infrastructure;
- observability tools;
- results of functional, load, or resilience checks;
- ADRs and a `03_ARCHITECTURE.md` document.

The last two items are intentionally absent: architecture and decisions should not be documented as facts before substantive design begins.

## Currently Accepted Positions

| Area | Current Position |
|---|---|
| Purpose | Learning and portfolio backend project |
| Domain | Narrow: event ingestion and processing |
| Main platform | ASP.NET Core |
| Primary persistent storage | PostgreSQL as the project's selected direction |
| Development approach | Sequential, verifiable vertical slices |
| Architectural complexity | Added only to solve a stated problem |
| Cloud direction | AWS; specific services have not been selected |

## Open Questions for the Next Stage

Only the minimum required details need to be determined before or at the beginning of Stage 1:

1. What does the simplest event sufficient to demonstrate ingestion look like?
2. Does the first version accept one object, a batch, or both?
3. Which fields and validation rules are mandatory in the first version?
4. Which HTTP results represent successful ingestion, a client error, and an internal failure?
5. What is the minimal way to verify that data was persisted?
6. How will PostgreSQL run during local development?
7. What minimal set of unit/integration tests demonstrates the stage result?

The answers should not attempt to resolve asynchronous delivery, Redis, AWS, or the final data model in advance.

## Decisions Not Yet Made

The following must not be treated as part of the current architecture:

- the specific data-access technology and set of libraries;
- the final event format and table schema;
- the queue technology;
- delivery guarantees and the idempotency model;
- the role of Redis;
- the authentication and authorization model;
- the specific AWS services;
- the container and network topology;
- the CI/CD, logging, metrics, and tracing tools;
- performance targets, instance count, and SLA.

## Stage Status

| Stage | Status | Actual Result |
|---|---|---|
| 0. Documentation foundation | Completed (initial version) | Four initial context documents created |
| 1. Basic data ingestion | Not started | No implementation |
| 2. Asynchronous processing | Not started | No implementation |
| 3. Reliability | Not started | No implementation |
| 4. Scaling and load | Not started | No measurements |
| 5. AWS and CI/CD | Not started | No infrastructure |
| 6. Production hardening | Not started | No operational mechanisms |

## Recommended Next Outcome

Prepare and implement the thinnest vertical slice of Stage 1:

> A locally running ASP.NET Core API accepts a minimal valid event, rejects an invalid one, persists the accepted event in PostgreSQL, and makes it possible to verify the result with an automated integration test.

Before writing code, it is sufficient to agree on the minimal contract and the completion criteria for this slice. If the solution structure, data-access method, or local execution approach has several meaningful alternatives, discuss it separately; an ADR is needed only for a decision with long-term consequences.

## How to Continue in a New Session

Before starting work, read:

1. `docs/00_PROJECT_CONTEXT.md`;
2. `docs/01_SOURCE_OF_TRUTH.md`;
3. `docs/02_ROADMAP.md`;
4. this file.

A suitable prompt for the next session:

> The project does not yet have an implementation. Read the four documents in `docs`. Help design the minimal vertical slice for Stage 1. Separate accepted facts from proposals, do not select a queue, Redis, or AWS services, and do not expand the domain. First define the minimal contract and verifiable completion criteria.

## Update Rule

This document must be updated after every meaningful change in project state. It should contain only current facts, open questions, and the next outcome. The history of important decisions belongs in ADRs and the repository history, not accumulated here as a development diary.
