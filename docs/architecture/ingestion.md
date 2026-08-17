# Ingestion Architecture

**Status:** Accepted design; partially implemented

## Event record

Each event uses [Event Contract v1](../contracts/event-ingestion-v1.md). Its envelope contains `type`, `source`, `occurredAt`, and an opaque JSON-object `payload`. Batch transport does not change these event-level semantics.

The implemented NDJSON reader accepts a `Stream`, frames and parses records
incrementally, and exposes the ordered outcomes as `IAsyncEnumerable`. A record result
contains its one-based physical record number and either an independently owned
untrusted `JsonElement` or a malformed marker. It has no Event Contract, HTTP, or
persistence responsibility.

The implemented `EventEnvelope`/validation boundary accepts the parsed untrusted
`JsonElement`. It returns either an immutable valid `EventEnvelope` or
EventEnvelope-specific coded validation errors. `EventEnvelope` contains non-empty
`Type` and `Source` strings, a UTC `DateTime` `OccurredAt`, and an opaque object-valued
`JsonElement` `Payload`. The payload is cloned while the envelope is constructed, so
it does not borrow the lifetime of the reader result.

The implemented Step 3 persistence boundary accepts one already-formed collection of
valid `EventEnvelope` values. Its EF Core implementation translates them directly to
the separate `EventRecord` representation and persists them through the existing
`PulseFlowDbContext`. The implemented Step 4 handler validates parsed records, forms
and stores ordered chunks, and returns normal-completion accounting. Application
database wiring and the HTTP endpoint remain unimplemented.

## Batch transport

Batch ingestion uses NDJSON with one independent Event Contract v1 record per line. A completely received record is a separate unit of ingestion:

- LF and CRLF terminate records;
- a blank line is an empty malformed record rather than ignored input;
- a syntactically complete final JSON value at clean end-of-stream is a record even
  without a trailing newline;
- incomplete or malformed final JSON is a malformed record and is not repaired;
- records are numbered from one in physical stream order;
- cancellation propagates through normal .NET cancellation semantics and is not
  classified as malformed input;
- valid records may be accepted independently of other records in the upload;
- one malformed record does not automatically reject other valid records;
- if the upload is interrupted during the last record, earlier completely received records remain eligible for processing;
- incomplete or malformed records are not repaired heuristically.

The transport choice and exact framing rules are recorded in
[ADR 0001](../decisions/0001-use-ndjson-for-batch-ingestion.md) and
[ADR 0004](../decisions/0004-define-ndjson-record-framing.md).

## Validation and persistence

The accepted Stage 1 ingestion pipeline is:

```text
NDJSON stream
    ↓
NDJSON record
    ↓
JSON syntax parsing
    ↓
untrusted parsed JSON record
    ↓
Event Contract v1 validation
    ↓
EventEnvelope
    ↓
configurable in-memory chunk
    ↓
PostgreSQL transaction
    ↓
commit = accepted
```

The NDJSON reader reads the stream sequentially with a fixed-size I/O buffer and a
buffer for only the current record. Each completed record is parsed once into an
independently owned untrusted `JsonElement`; JSON parsing determines only syntax
validity. A malformed record is yielded as a simple record-level outcome, and later
records remain readable when the stream itself remains readable.

Separate contract validation inspects the already-parsed representation and
determines whether it satisfies Event Contract v1: `type`, `source`, `occurredAt`, and
the requirement that `payload` is a JSON object. It does not serialize and parse the
record again. Only successful contract validation constructs `EventEnvelope`; the
opaque contents of `payload` are not interpreted. The rationale and consequences are
recorded in [ADR 0003](../decisions/0003-construct-event-envelope-after-contract-validation.md).

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

`IngestEventsHandler` receives `EventEnvelopeValidator`, `IEventChunkStore`, and a
positive integer chunk capacity directly through its constructor. It consumes
`IAsyncEnumerable<NdjsonRecordResult>` and retains only the current record, current
chunk, and accepted/rejected counters. Malformed and contract-invalid records both
increment `Rejected`; they do not enter a chunk. Each full chunk and the final
non-empty partial chunk are stored in input order. `Accepted` advances only after the
corresponding store call completes successfully.

On normal completion the handler returns `IngestEventsResult` containing only
`Accepted` and `Rejected`. If a store call throws, the exception propagates unchanged,
processing stops, and no result is returned. Earlier successfully stored chunks remain
durable, while the failing chunk is not accepted. This is the deliberate simple
failure model accepted in
[ADR 0006](../decisions/0006-keep-ingestion-handler-failure-propagation-simple.md),
not a claim of request-level atomicity. HTTP behavior and the retry/idempotency problem
remain unresolved.

The rationale and consequences are recorded in [ADR 0002](../decisions/0002-use-chunked-postgresql-persistence-for-ingestion.md).

The implemented persistence boundary is structurally:

```text
IEventChunkStore
    ↓
EfCoreEventChunkStore
    ↓
PulseFlowDbContext
    ↓
PostgreSQL
```

`IEventChunkStore.StoreAsync` accepts an `IReadOnlyCollection<EventEnvelope>` and
cancellation. The EF Core implementation creates one `EventRecord` per envelope,
adds the complete supplied collection, and calls `SaveChangesAsync` once. Successful
completion is the durability boundary for that supplied chunk; cancellation and
persistence failures propagate.

The direct translation is:

```text
EventRecord.Id          <- Guid.NewGuid()
EventRecord.Type        <- EventEnvelope.Type
EventRecord.Source      <- EventEnvelope.Source
EventRecord.OccurredAt  <- EventEnvelope.OccurredAt
EventRecord.ReceivedAt  <- DateTime.UtcNow
EventRecord.PayloadJson <- EventEnvelope.Payload JSON
```

The payload is passed to the existing PostgreSQL `jsonb` mapping, which preserves its
JSON semantics rather than original whitespace or property order. `Id` and
`ReceivedAt` are persistence-only metadata and do not become part of `EventEnvelope`.
The generation decision is recorded in
[ADR 0005](../decisions/0005-generate-event-persistence-metadata-in-application.md).

## Not yet defined

- record, upload, and record-count limits;
- compression;
- HTTP route, status codes, and response formats, including partial success;
- authentication and authorization;
- idempotency, deduplication, and client retry behavior;
- downstream processing-transfer technology;
- the concrete chunk size and its configuration source;
- HTTP behavior when a database failure occurs after one or more chunks have committed;
- retry and idempotency behavior for an overall failure after earlier chunks committed;
- PostgreSQL retry strategy, EF Core execution strategy, and transaction isolation level;
- application database DI wiring and production migration execution;
- the concrete validation library or framework.

RabbitMQ, Redis, polling, and queue or stream technologies are not accepted parts of the architecture at this point.
