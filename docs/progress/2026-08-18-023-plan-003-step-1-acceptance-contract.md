# Checkpoint: PLAN 003 Step 1 asynchronous acceptance contract decided

**Date:** 2026-08-18

## Starting point

Stage 1 remained the implemented synchronous ingestion path. RabbitMQ was accepted
as the future Stage 2 work-transfer broker by ADR 0008, but the broker message
representation and the minimum HTTP acceptance boundary had not yet been decided.
PLAN 003 Step 1 was not started.

## What changed

- Completed PLAN 003 Step 1 as a documentation-only decision.
- Selected the complete raw NDJSON batch body as the RabbitMQ payload for the first
  Stage 2 slice. `PulseFlow.Api` will not parse individual records or apply Event
  Contract v1 validation before publishing it.
- Selected RabbitMQ publication confirmation as the HTTP acceptance boundary: the API
  returns HTTP `202 Accepted` only after successful publication is confirmed.
- Recorded that a RabbitMQ publication failure must not return HTTP `202 Accepted`.
- Recorded that Step 2 will remove HTTP and integration tests whose specific purpose
  is to preserve the obsolete Stage 1 synchronous `200` accounting contract.
- Recorded that parsing, validation, persistence, and other tests that remain valid
  behind the RabbitMQ boundary remain in scope.
- Created ADR 0009 for the accepted representation and acceptance-boundary choices.

## Resulting repository state

The implemented repository remains Stage 1: `POST /api/events` parses NDJSON,
validates Event Contract v1, persists valid records to PostgreSQL, and returns
synchronous HTTP `200` accounting. RabbitMQ and `EventParserConsumer` do not yet
exist in code or configuration.

No production code, tests, package references, Docker configuration, or application
configuration changed.

## Decisions made

- The first Stage 2 RabbitMQ message is the complete raw NDJSON batch body, not an
  external-storage reference or identifier.
- HTTP `202 Accepted` follows confirmed RabbitMQ publication, not merely an attempted
  publication.
- The synchronous Stage 1 `total`/`accepted`/`rejected` HTTP accounting contract is
  not retained for the replacement asynchronous endpoint.

The rationale, alternatives, consequences, and remaining reliability questions are
recorded in [ADR 0009](../decisions/0009-define-stage-2-rabbitmq-batch-acceptance-boundary.md).

## Verification

Commands run from the repository root after the documentation changes:

```powershell
pwsh ./scripts/check-project-docs.ps1
```

Results:

- `pwsh ./scripts/check-project-docs.ps1` could not start because PowerShell 7 is not
  installed in this environment.
- The equivalent documentation-validation script completed successfully with the
  available Windows PowerShell host: `& .\scripts\check-project-docs.ps1`.

## Intentionally unresolved

- RabbitMQ topology, routing keys, acknowledgement/requeue behavior, retries,
  dead-letter queues, and delivery guarantees.
- Outbox, idempotency, deduplication, and client retry behavior.
- Request, batch, and RabbitMQ message-size limits and compression.
- The exact HTTP `202` response body and any batch-status/query contract.
- Deployment topology and consumer capacity configuration.

## Next recommended step

Continue with PLAN 003 Step 2: implement API-to-RabbitMQ asynchronous acceptance.
