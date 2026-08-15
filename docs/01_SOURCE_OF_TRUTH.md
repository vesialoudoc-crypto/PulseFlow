# Source of Truth

## Purpose

This document records the stable truth about the product: what is being built, for whom, which properties are mandatory, and where the project boundaries lie.

It intentionally does not define a specific architecture, cloud services, queue technology, exact data schema, or infrastructure parameters. Such decisions must be made separately and may change without rewriting the essence of the product.

## Product Statement

The system accepts event data from external clients, including batches, validates it, and provides controlled processing with a way to retrieve or verify the result.

The system is being built as a production-style backend: its behavior during normal operation, repeated requests, partial failures, and increasing load must be observable, verifiable, and explainable.

## Users and Roles

Only generalized roles are currently defined:

- an **external client** submits events and receives a clear result from the API interaction;
- a **data consumer** reads or verifies the processing result when the current stage provides that capability;
- an **operator/developer** observes system state, diagnoses errors, and manages deployment.

The specific business model for users, tenants, and access permissions has not yet been defined.

## Mandatory Capabilities

When complete, the system must:

1. Accept individual events and/or batches of events through a documented interface.
2. Validate input data and return distinguishable results for valid and invalid requests.
3. Persist accepted data, or otherwise transfer it reliably for processing, without silent loss.
4. Process accepted data separately from the client request once an asynchronous path is introduced.
5. Have defined and tested behavior for repeated delivery, transient failures, and partially completed operations.
6. Provide a minimal way to inspect processing state or results.
7. Support multiple instances of components for which horizontal scaling is claimed.
8. Provide enough signals to answer: Is the system working? Where did an error occur? What was the processing result?
9. Have a reproducible path from a change to a tested and deployed artifact.

Exact guarantees for delivery, consistency, and processing time must be defined before the corresponding stage is implemented.

## Key Quality Attributes

### Reliability

Data accepted by the system must not disappear silently. Errors must result in a defined state available for diagnosis. Retry and unrecoverable-message policies must be bounded and verifiable.

### Correctness Under Repetition and Concurrency

Repeated requests and delivery of the same message more than once are normal distributed-system scenarios. The project must choose an explicit semantic and demonstrate it with tests. Concurrent processing must not cause inexplicable state corruption.

### Scalability

The architecture must make it possible to explore horizontal scaling. Scalability claims must be supported by load measurements, not only by a diagram.

### Observability

Critical paths and failures must be visible through structured logs, metrics, tracing, and health checks to the extent justified by the project stage.

### Security

Input data is untrusted. Secrets must not be stored in source code. Authentication, authorization, rate limiting, and infrastructure protection are introduced as the corresponding risk surface emerges and are documented explicitly.

### Reproducibility

Local execution, testing, and deployment must be documented and automated where practical. Significant results from load and resilience experiments must be reproducible.

## In Scope

- an ASP.NET Core HTTP API for data ingestion;
- data persistence with PostgreSQL;
- a minimal synchronous path followed by extraction of asynchronous processing;
- data validation, error handling, and clear result states;
- investigation of repetition, idempotency, concurrency, and partial failures;
- load testing and horizontal scaling;
- justified investigation of Redis and a queue technology;
- deployment to AWS;
- CI/CD;
- observability, operational checks, and production hardening;
- automated tests and documentation of accepted decisions.

## Out of Scope

- a complex user interface;
- a full analytics platform, BI, or data visualization;
- a large set of business entities and processes;
- mobile applications;
- artificial creation of microservices merely to increase the component count;
- operation at a scale that requires substantial ongoing expense;
- promises of specific SLAs before measurements and operational experience exist;
- use of every technology under study regardless of its usefulness.

## Established

- The project is a learning and portfolio project.
- The domain remains narrow: event ingestion and processing.
- The main backend is built with ASP.NET Core.
- PostgreSQL is the primary direction for studying persistent storage.
- Reliability, concurrency, scaling, AWS, CI/CD, and observability are learning and demonstration goals.
- Development proceeds through sequential, verifiable stages.

## Not Yet Decided

- the event format and final schema;
- the public API contract and batch-size limits;
- the exact data model and migration strategy;
- the boundary between synchronous and asynchronous work;
- the queue technology and delivery guarantees;
- whether Redis is needed and what role it would serve;
- the client identity and access-control model;
- the specific AWS services and network topology;
- the number and size of instances;
- target throughput and latency metrics;
- the specific CI/CD and observability tools.

These items are not gaps that must be filled immediately. Each should be resolved when required by the nearest verifiable stage.

## Project Completion Criteria

The project can be considered to have achieved its primary goal when:

- the complete path from ingestion to processing result is implemented and documented;
- behavior for invalid data, repetition, and selected failure types is covered by tests;
- horizontal execution has been verified through a load experiment;
- the system is deployed to AWS reproducibly;
- change delivery includes automated checks;
- the system state is observable and common errors can be diagnosed;
- key decisions and their trade-offs are recorded;
- the README allows another developer to run and evaluate the project.

## Document Change Rule

This file changes only when the product's essence, boundaries, or mandatory properties change. Replacing one technical component with another usually does not require a change to the Source of Truth and should instead be reflected in architecture documentation, ADRs, and `04_CURRENT_STATE.md`.
