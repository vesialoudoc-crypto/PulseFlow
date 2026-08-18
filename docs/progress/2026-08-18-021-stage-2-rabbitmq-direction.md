# Checkpoint: Accepted Stage 2 RabbitMQ direction and PLAN 003

**Date:** 2026-08-18

## Starting point

Stage 1 was completed and verified in the preceding checkpoint. The implemented path
remained synchronous:

```text
HTTP -> NDJSON parse -> Event Contract v1 validation -> PostgreSQL
```

Stage 2 had not started. RabbitMQ, the asynchronous processing boundary, and the
specific consumer responsibility were not previously accepted implementation
decisions.

## What changed

- Accepted RabbitMQ as the Stage 2 work-transfer broker through
  [ADR 0008](../decisions/0008-use-rabbitmq-to-decouple-http-ingestion-from-parsing.md).
- Recorded the intentional Stage 2 boundary change: the API will accept batches for
  asynchronous processing without parsing individual NDJSON records or applying Event
  Contract v1 validation; those responsibilities move to `EventParserConsumer` after
  RabbitMQ.
- Created [PLAN 003](../plans/003-stage-2-asynchronous-ingestion.md), which sequences
  the next small design and implementation steps without claiming implementation.
- Updated the Stage 2 roadmap requirements and the ingestion architecture document to
  distinguish the completed Stage 1 implementation from the accepted Stage 2 target.
- Left `docs/01_SOURCE_OF_TRUTH.md` unchanged because the accepted direction does not
  change the product boundaries or mandatory properties.

## Resulting repository state

Stage 1 remains **Completed**. Stage 2 remains **Not started**: RabbitMQ,
`EventParserConsumer`, an asynchronous API acknowledgement path, and all associated
application or infrastructure code do not exist in the repository.

The accepted Stage 2 target is:

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

The scaling motivation is many external clients: RabbitMQ buffers accepted ingestion
work, decouples HTTP ingestion throughput from parsing and validation throughput, and
allows parser consumers to scale independently from `PulseFlow.Api`. RabbitMQ is not
an HTTP load balancer and is not being added for portfolio breadth.

No production code, tests, Docker configuration, package references, or application
configuration changed.

## Decisions made

- [ADR 0008](../decisions/0008-use-rabbitmq-to-decouple-http-ingestion-from-parsing.md)
  accepts RabbitMQ as the required Stage 2 broker for decoupling HTTP batch ingestion
  from NDJSON parsing and Event Contract v1 validation.
- `EventParserConsumer` is the planning name for the component with the concrete
  responsibility of asynchronous NDJSON parsing, Event Contract v1 validation, and
  PostgreSQL persistence.
- The Stage 1 synchronous HTTP 200 `{ total, accepted, rejected }` response cannot
  remain the final Stage 2 contract unchanged. A likely HTTP 202 asynchronous
  acknowledgement is recorded as a direction, not a finalized response contract.

## Verification

Commands run from the repository root after staging the documentation changes:

```powershell
pwsh ./scripts/check-project-docs.ps1
& .\scripts\check-project-docs.ps1
```

Results:

- `pwsh ./scripts/check-project-docs.ps1` could not start because PowerShell 7 is not
  installed in this environment.
- The same repository documentation script completed successfully with the available
  Windows PowerShell host: `& .\scripts\check-project-docs.ps1`.

## Intentionally unresolved

- Whether RabbitMQ receives a complete raw NDJSON batch or a reference to separately
  stored raw data.
- Batch, request, and message-size limits; compression.
- RabbitMQ topology, routing keys, acknowledgement/requeue semantics, retries,
  dead-letter queues, and delivery guarantees.
- Outbox, idempotency, deduplication, Redis, batch-status persistence, public status
  or query endpoints, and deployment topology.
- Reliability mechanisms and delivery semantics beyond the first Stage 2 slice.

## Next recommended step

Execute PLAN 003 Step 1: make the minimum asynchronous acceptance and publishing
contract decision required before production code replaces the Stage 1 synchronous
HTTP boundary.
