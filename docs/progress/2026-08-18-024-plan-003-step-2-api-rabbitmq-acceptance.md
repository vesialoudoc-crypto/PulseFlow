# Checkpoint: PLAN 003 Step 2 API-to-RabbitMQ acceptance implemented

**Date:** 2026-08-18

## Starting point

PLAN 003 Step 1 had accepted the first asynchronous acceptance contract: the API
would publish the complete raw NDJSON request body to RabbitMQ and return HTTP `202`
only after publisher confirmation. The implemented endpoint was still the Stage 1
synchronous path, which parsed records, validated Event Contract v1, persisted valid
events to PostgreSQL, and returned HTTP `200` accounting.

## What changed

- Added the official `RabbitMQ.Client` 7.2.2 package.
- Added the application-owned `IIngestionBatchPublisher` boundary, which accepts raw
  batch bytes without exposing RabbitMQ client types.
- Added `RabbitMqIngestionBatchPublisher`. It uses a required RabbitMQ connection
  string, declares the configured durable named queue through RabbitMQ's default
  exchange, publishes the exact raw bytes with `application/x-ndjson`, and awaits a
  publisher confirmation before completing.
- Replaced the `POST /api/events` Stage 1 HTTP orchestration with complete raw-body
  forwarding to `IIngestionBatchPublisher`. It now returns body-less HTTP `202`
  after successful publication and does not call the reader, validator, handler, or
  PostgreSQL persistence path.
- Added validated `RabbitMq:QueueName` configuration. RabbitMQ connectivity is
  required through `ConnectionStrings:RabbitMq`, which supports the matching
  `ConnectionStrings__RabbitMq` environment-variable override. No credential is
  committed.
- Removed the obsolete `EventIngestionTests` HTTP/integration suite. Its assertions
  covered the replaced Stage 1 HTTP `200` accounting and direct persistence behavior;
  independent parser, validator, handler, and PostgreSQL persistence tests remain.
- Added focused HTTP acceptance tests using a recording/failing publisher for raw-byte
  forwarding, successful `202`, malformed-NDJSON acceptance, and safe publication
  failure handling.
- Updated the PLAN 003 Step 2 status, the Stage 2 roadmap status, the active
  ingestion architecture, and the Event Contract v1 HTTP-boundary documentation.

## Resulting repository state

The implemented request path is now:

```text
Client -> POST /api/events -> raw NDJSON bytes -> RabbitMQ -> HTTP 202 after confirmation
```

The named durable queue and default-exchange routing are the smallest local topology
needed to publish a queue-bound batch in this slice. They do not establish a final
topology, retry, requeue, dead-letter, delivery, or end-to-end durability guarantee.

The Stage 1 `NdjsonRecordReader`, `EventEnvelopeValidator`, `IngestEventsHandler`,
`IEventChunkStore`, EF Core store, and their valid tests remain in the repository for
the later consumer path. They are no longer called by the HTTP endpoint.

`EventParserConsumer` is not implemented. RabbitMQ messages are not parsed,
validated, or persisted by a consumer in this checkpoint.

## Verification

Commands run from the repository root:

```powershell
dotnet build PulseFlow.slnx -warnaserror
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- Build succeeded with 0 warnings and 0 errors.
- The full solution test suite passed: 97 passed, 0 failed, 0 skipped (64 unit and
  33 integration tests). The integration tests include the focused HTTP acceptance
  tests and the preserved PostgreSQL-backed tests.
- PowerShell 7 (`pwsh`) is not installed in this environment, so the required command
  could not start. The equivalent repository script completed successfully with the
  available Windows PowerShell host: `& .\scripts\check-project-docs.ps1`.

## Decisions made

- Step 2 implements the API side of the raw-batch and publisher-confirmation boundary
  accepted by [ADR 0009](../decisions/0009-define-stage-2-rabbitmq-batch-acceptance-boundary.md).
- The queue declaration and default-exchange routing are a narrow implementation
  choice necessary for this slice, not an accepted final RabbitMQ topology. No new
  ADR was created for that local choice.

## Intentionally unresolved

- `EventParserConsumer`, consumer acknowledgement/requeue behavior, and consumer
  persistence composition.
- Message and batch limits, compression, routing conventions beyond this direct
  queue publish, retries, DLQ, delivery guarantees, Outbox, idempotency,
  deduplication, and batch tracking.
- RabbitMQ deployment topology and observability.

## Next recommended step

Implement only PLAN 003 Step 3: add `EventParserConsumer` that receives the raw
RabbitMQ batch and reuses the existing parsing, validation, and persistence boundaries.
