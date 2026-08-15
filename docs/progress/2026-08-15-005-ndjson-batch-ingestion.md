# Checkpoint: NDJSON batch ingestion accepted

**Date:** 2026-08-15

## Starting point

Stage 1 was in progress with Event Contract v1 accepted at the individual-record level. The API and persistence path were not implemented, and batch ingestion semantics were unresolved.

The project needed a batch format suitable for clients that can remain offline and later upload a large accumulation of diagnostic events. A damaged record or an interrupted final record must not erase the value of earlier completely received valid events.

## What changed

- Added [ADR 0001](../decisions/0001-use-ndjson-for-batch-ingestion.md), comparing a JSON array, heuristic JSON repair, and NDJSON.
- Accepted NDJSON with one Event Contract v1 record per line as the batch transport format.
- Established a completely received record as an independent unit of ingestion.
- Recorded the accepted design in `docs/architecture/ingestion.md`.
- Updated the Stage 1 roadmap to distinguish accepted framing and independent-record semantics from unresolved limits and HTTP response behavior.

## Resulting state

NDJSON framing and record-level independence are accepted architecture but are not implemented. Completely received valid records may be accepted independently; malformed or truncated records are not heuristically repaired and do not automatically invalidate other completely received valid records.

Event Contract v1 remains unchanged: every record contains `type`, `source`, `occurredAt`, and an opaque JSON-object `payload`.

No ingestion endpoint, parser, persistence implementation, transaction strategy, messaging component, or partial-success response has been added or selected.

## Verification

Run from the repository root:

```powershell
dotnet build PulseFlow.slnx
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- the solution builds with no warnings or errors;
- the test command succeeds and reports no discoverable tests in either test project;
- the documentation check completes without warnings;
- the roadmap, architecture summary, ADR, and latest checkpoint consistently distinguish accepted NDJSON framing from deferred implementation and reliability choices.

## Decisions made

- Batch ingestion uses NDJSON with one Event Contract v1 object per record.
- A completely received record is an independent unit of ingestion.
- Valid records may be accepted independently of malformed records in the same upload.
- Earlier completely received records remain eligible for processing if the upload is interrupted during a later record.
- Malformed or incomplete records are not repaired heuristically.
- Event Contract v1 remains unchanged by the transport decision.

See [ADR 0001](../decisions/0001-use-ndjson-for-batch-ingestion.md) for alternatives and consequences.

## Still unresolved

- Maximum record size, upload size, and record count.
- Compression.
- HTTP route, status codes, and success, partial-success, and error response formats.
- Authentication and authorization.
- Idempotency, deduplication, and client retry behavior.
- RabbitMQ, Redis, polling, and queue or stream technology.
- PostgreSQL transaction strategy and persistence implementation.
- The mechanism for transferring accepted events to later processing.

## Next recommended step

Define only the remaining HTTP behavior and persistence details required for the smallest verifiable Stage 1 NDJSON ingestion slice. Do not infer a messaging technology, delivery guarantee, idempotency model, or transaction strategy from this framing decision.
