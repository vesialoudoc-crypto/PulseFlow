# Checkpoint: PLAN 003 Step 3 parser consumer

**Date:** 2026-08-18

## Starting point

PLAN 003 Steps 1 and 2 had established the asynchronous HTTP acceptance boundary:
`PulseFlow.Api` published the complete raw NDJSON batch to RabbitMQ and returned HTTP
202 after publisher confirmation. The Stage 1 NDJSON reader, Event Contract v1
validator, ingestion handler, and PostgreSQL chunk store remained reusable but no
RabbitMQ consumer existed.

## What changed

- Added `EventParserConsumer` as an ASP.NET Core hosted `BackgroundService`.
- Added the narrow RabbitMQ-specific consumer-channel adapter. It creates one consumer
  channel from the existing long-lived connection, consumes the configured durable
  queue manually, and owns that channel's shutdown disposal.
- Added per-delivery scope creation in `EventParserConsumer`. Each delivery resolves
  the scoped `IngestEventsHandler` and its existing EF Core persistence dependencies,
  exposes the raw message bytes through a stream, and reuses `NdjsonRecordReader`.
- Added manual acknowledgement after successful handler completion only. Processing
  exceptions are logged without a successful acknowledgement, reject, negative
  acknowledgement, retry, or requeue command.
- Added focused unit tests for the existing parsing/persistence path, Stage 1
  malformed and invalid-record rejection behavior, successful acknowledgement, and
  absence of acknowledgement when persistence fails.
- Added a bounded PostgreSQL integration test for raw batch -> parser consumer ->
  existing parsing/validation/handler -> PostgreSQL. It uses a RabbitMQ-facing fake;
  running a real RabbitMQ broker is intentionally deferred to PLAN 003 Step 4.

## Resulting repository state

The implemented path is now:

```text
Client -> PulseFlow.Api -> RabbitMQ -> EventParserConsumer
    -> NDJSON parsing -> Event Contract v1 validation -> PostgreSQL
```

The API remains unchanged: `POST /api/events` copies raw NDJSON bytes, publishes them
to RabbitMQ, and returns HTTP 202 only after confirmed publication. It does not parse,
validate, or persist individual event records.

`EventParserConsumer` uses the application connection but its own long-lived channel,
so it does not share the publisher channel. It creates a DI scope for each delivery,
passes application shutdown cancellation through to the reader and handler, and sends
manual acknowledgement only after successful persistence handling. On a normal
processing exception it leaves the message unacknowledged; the specific broker outcome
after a channel or connection closes is not selected as a retry or requeue policy.

## Verification

Commands run from the repository root:

```powershell
dotnet build PulseFlow.slnx -warnaserror
dotnet test PulseFlow.slnx
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/check-project-docs.ps1
```

Results:

- Build succeeded with 0 warnings and 0 errors.
- The solution tests passed: 102 passed, 0 failed, 0 skipped (68 unit and 34
  integration tests).
- `pwsh` was unavailable in the environment, so the required documentation-validation
  script was run with Windows PowerShell and completed successfully.

## Decisions made

- A narrow RabbitMQ-specific adapter was added solely to own the consumer channel and
  make acknowledgement behavior testable without a broker. It does not introduce a
  generic messaging or business-processing framework.
- The no-successful-acknowledgement behavior on processing failure is the smallest
  explicit Step 3 behavior. It intentionally does not decide broker retry, requeue,
  dead-letter, poison-message, or delivery-guarantee behavior, so no ADR was created.

## Intentionally unresolved

- Retry, requeue, dead-letter, poison-message, and final acknowledgement policies.
- Outbox, idempotency, deduplication, and delivery guarantees.
- Batch status/result storage and public status/query APIs.
- Real-broker end-to-end verification, parser-consumer capacity configuration, and
  structural competing-consumer verification in PLAN 003 Step 4.

## Next recommended step

Implement only PLAN 003 Step 4: prove the controlled complete asynchronous path with
RabbitMQ and establish independently configurable parser-consumer capacity without
adding reliability mechanisms.
