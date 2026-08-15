# ADR 0002: Use Chunked PostgreSQL Persistence for Ingestion

**Date:** 2026-08-15

## Status

Accepted

## Context

PulseFlow batch ingestion uses NDJSON with independent Event Contract v1 records, as established by [ADR 0001](0001-use-ndjson-for-batch-ingestion.md). An upload may contain a potentially large accumulation of events from a client that remained offline.

Holding the entire HTTP upload in memory before persistence scales memory consumption with upload size, delays durability until the upload ends, and risks losing all otherwise valid records if a late failure prevents the single transaction from committing.

At the other extreme, committing one PostgreSQL transaction for every event creates a database round trip and transaction overhead for every valid record. That approach preserves a narrow record-level commit boundary but is likely to limit ingestion throughput unnecessarily.

The persistence strategy must preserve record-level parsing and validation, allow already durable work to survive later failures, avoid unbounded buffering relative to upload size, and define an unambiguous acceptance boundary.

## Considered Options

### 1. Buffer the entire upload and persist it in one transaction

Advantages:

- One transaction gives the complete upload an all-or-nothing persistence outcome.
- Commit and transaction setup overhead occur once per upload.
- Aggregate success or failure accounting is conceptually simple.

Disadvantages:

- Memory consumption grows with the complete upload size.
- Persistence cannot begin until the upload has been fully received and buffered.
- A late malformed record, connection interruption, or database failure can prevent every record in the upload from becoming durable.
- It conflicts with the recovery goal of retaining earlier completely received valid records.
- Large transactions can hold database resources for longer and create a large failure unit.

### 2. Persist every valid record independently

Advantages:

- Memory usage can remain close to one record.
- Each successful record transaction has a simple durability boundary.
- A persistence failure for one record need not include other valid records.
- Persistence can begin as soon as the first valid record is available.

Disadvantages:

- Every record requires transaction setup, commit, and associated database interaction.
- Database round trips and commit overhead grow directly with record count.
- The approach can sacrifice throughput for a transaction boundary narrower than Stage 1 requires.
- High event volume can create unnecessary pressure on the database and connection pool.

### 3. Stream records and persist configurable chunks

Advantages:

- Memory usage is bounded by record and chunk limits rather than by complete upload size.
- One transaction amortizes commit and round-trip overhead across multiple records.
- Parsing, validation, and persistence can begin before the complete upload arrives.
- Earlier committed chunks remain durable if a later record or chunk fails.
- Chunk size can be tuned using load measurements without changing the public contract.

Disadvantages:

- One upload can span multiple PostgreSQL transactions and therefore have a partial persistence outcome.
- Records that validated successfully but belong to an uncommitted chunk are not accepted.
- The server needs record-level and chunk-level result accounting.
- Operational configuration and measurement are required to avoid chunks that are too small or too large.
- HTTP behavior becomes more complex when a database failure occurs after earlier chunks have committed.

## Decision

PulseFlow Stage 1 ingestion streams NDJSON records sequentially. Each record is parsed and validated independently against the Event Contract v1 envelope. Malformed or invalid records are not placed in a persistence chunk, and the opaque internal structure of `payload` is not interpreted.

Valid records accumulate in a configurable in-memory chunk. The server persists each chunk within one PostgreSQL transaction. After a successful commit, it continues reading and forming the next chunk. The server does not buffer the complete upload before persistence and does not commit one transaction per event.

The durability and acceptance boundary is:

> PostgreSQL commit is the durability boundary for Stage 1 ingestion acceptance.

A record in memory or in a forming chunk is not accepted. Records become accepted only when the transaction containing their chunk commits successfully. A later failure does not roll back earlier committed chunks. If the current chunk does not commit, none of its records are accepted.

Record-level transport semantics from ADR 0001 remain in effect: malformed or invalid records do not automatically reject other valid records. Records within one persistence chunk share a physical commit outcome, which is distinct from their independent parsing and validation outcome.

The chunk size is configurable operationally and is not part of the public contract. This ADR does not select a concrete value; load measurements must inform later tuning.

Database constraints are an additional integrity and backstop layer. They do not replace Event Contract v1 validation at the ingestion boundary. This ADR does not select a validation library or framework.

## Consequences

Positive consequences:

- Memory usage is bounded relative to upload size by the configured chunking strategy.
- Transaction and database round-trip overhead is lower than with one commit per record.
- Persistence can begin without waiting for the complete upload.
- Earlier committed chunks remain durable when a later record, chunk, or upload operation fails.
- Chunk size can be tuned later using load-test evidence without changing the external contract.
- The PostgreSQL commit boundary gives acceptance a durable and testable meaning.

Negative consequences and new responsibilities:

- One request can use multiple PostgreSQL transactions.
- A failure after earlier commits produces partial persistence for the upload.
- The server must account for accepted, rejected, and not-committed records.
- An uncertain HTTP outcome and client retry can resend records that were already accepted; idempotency and deduplication require a separate decision.
- A chunk size that is too small increases transaction overhead, while one that is too large increases memory use and the number of records affected by one failed commit.
- Chunk size requires operational configuration and performance measurement.
- HTTP behavior for a database failure after partial persistence requires a separate contract decision.

## Deferred Decisions

This ADR does not decide:

- the concrete chunk size or its configuration source;
- the HTTP response or status code for a database failure during an upload;
- PostgreSQL retry strategy;
- EF Core execution strategy;
- transaction isolation level;
- idempotency, deduplication, or client retry policy;
- RabbitMQ, Redis, or any queue or stream technology;
- compression or upload limits;
- authentication or authorization;
- the database schema, data-access implementation, or migration strategy;
- the validation library or framework.
