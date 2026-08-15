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

## Validation and persistence

The accepted Stage 1 ingestion pipeline is:

```text
NDJSON stream
    ↓
record parse
    ↓
record validation
    ↓
configurable in-memory chunk
    ↓
PostgreSQL transaction
    ↓
commit = accepted
```

The server reads the NDJSON stream sequentially. Each completely received record is parsed and validated independently against the Event Contract v1 envelope: `type`, `source`, `occurredAt`, and the requirement that `payload` is a JSON object. The opaque contents of `payload` are not interpreted.

Malformed or invalid records are excluded from persistence chunks. Request-level validation does not turn one invalid record into rejection of the complete upload. PostgreSQL constraints provide an additional integrity and backstop layer, but they do not replace validation at the ingestion boundary. No validation library or framework has been selected.

Valid records accumulate in a configurable in-memory chunk. Each chunk is persisted in one PostgreSQL transaction. After a successful commit, ingestion continues with the next chunk. The server neither holds the complete upload in memory before persistence nor commits one transaction per record.

The durability and acceptance boundary is:

> PostgreSQL commit is the durability boundary for Stage 1 ingestion acceptance.

Therefore:

- a parsed record held in memory or in a forming chunk is not yet accepted;
- records in a successfully committed chunk are accepted;
- an uncommitted chunk contains no accepted records;
- a failure in a later chunk does not roll back earlier committed chunks;
- a database failure can leave all records in the current chunk unaccepted even though they passed record validation.

The chunk size is operational configuration, not part of the public ingestion contract. No concrete value is accepted; it must be tuned later using load measurements.

The rationale and consequences are recorded in [ADR 0002](../decisions/0002-use-chunked-postgresql-persistence-for-ingestion.md).

## Not yet defined

- record, upload, and record-count limits;
- compression;
- HTTP route, status codes, and response formats, including partial success;
- authentication and authorization;
- idempotency, deduplication, and client retry behavior;
- downstream processing-transfer technology;
- the concrete chunk size and its configuration source;
- HTTP behavior when a database failure occurs after one or more chunks have committed;
- PostgreSQL retry strategy, EF Core execution strategy, and transaction isolation level;
- the database schema, data-access implementation, and migration strategy;
- the concrete validation library or framework.

RabbitMQ, Redis, polling, and queue or stream technologies are not accepted parts of the architecture at this point.
