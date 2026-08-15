# Ingestion Architecture

**Status:** Accepted design; not implemented

## Event record

Each event uses [Event Contract v1](../contracts/event-ingestion-v1.md). Its envelope contains `type`, `source`, `occurredAt`, and an opaque JSON-object `payload`. Batch transport does not change these event-level semantics.

## Batch transport

Batch ingestion uses NDJSON with one independent Event Contract v1 record per line. A completely received record is a separate unit of ingestion:

- valid records may be accepted independently of other records in the upload;
- one malformed record does not automatically reject other valid records;
- if the upload is interrupted during the last record, earlier completely received records remain eligible for processing;
- incomplete or malformed records are not repaired heuristically.

The rationale and consequences are recorded in [ADR 0001](../decisions/0001-use-ndjson-for-batch-ingestion.md).

## Not yet defined

- record, upload, and record-count limits;
- compression;
- HTTP route, status codes, and response formats, including partial success;
- authentication and authorization;
- idempotency, deduplication, and client retry behavior;
- downstream processing-transfer technology;
- PostgreSQL transaction strategy and persistence implementation.

RabbitMQ, Redis, polling, and queue or stream technologies are not accepted parts of the architecture at this point.
