# Ingestion Architecture

**Status:** Implemented Stage 1 architecture; accepted Stage 2 target recorded below

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

The implemented persistence boundary accepts one already-formed collection of valid
`EventEnvelope` values. Its EF Core implementation translates them directly to the
separate `EventRecord` representation and persists them through the existing
`PulseFlowDbContext`. The handler validates parsed records, forms and stores ordered
chunks, and returns normal-completion accounting. The controller and application
composition expose this flow over HTTP and wire it to PostgreSQL. Step 6 verifies the
real hosted HTTP-to-PostgreSQL path, including normal accounting, independent
malformed/contract-invalid records, full plus final partial chunks, and a later
persistence failure after an earlier real PostgreSQL commit.

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

The chunk size is operational configuration, not part of the public ingestion
contract. `IngestionOptions` binds `Ingestion:ChunkCapacity` and validates a positive
value when the application starts. The current value of 100 is a temporary initial
operational value, not a tuned performance conclusion; later load measurements must
inform tuning.

`IngestEventsHandler` receives `EventEnvelopeValidator`, `IEventChunkStore`, and a
positive integer chunk capacity directly through its constructor. It consumes
`IAsyncEnumerable<NdjsonRecordResult>` and retains only the current record, current
chunk, `Total`, and `Accepted`. `Total` advances for every processed input record.
Malformed and contract-invalid records do not enter a chunk. Each full chunk and the
final non-empty partial chunk are stored in input order, and `Accepted` advances only
after the corresponding store call completes successfully.

On normal completion the handler returns `IngestEventsResult` with `Total`,
`Accepted`, and `Rejected`, where `Rejected` is derived as `Total - Accepted` rather
than separately maintained. If a store call throws, the exception propagates
unchanged, processing stops, and no result is returned. Earlier successfully stored
chunks remain durable, while the failing chunk is not accepted. Because there is no
failure result, valid-but-uncommitted records are not misclassified by the derived
formula. This is the deliberate simple failure model accepted in
[ADR 0006](../decisions/0006-keep-ingestion-handler-failure-propagation-simple.md),
not a claim of request-level atomicity. The retry/idempotency problem remains
unresolved.

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

## HTTP and application composition

The implemented public boundary is `POST /api/events`, exposed by an ASP.NET Core
controller and restricted to `application/x-ndjson`. `EventsController` passes
`Request.Body` to `NdjsonRecordReader`, passes the resulting asynchronous sequence to
`IngestEventsHandler`, supplies request cancellation to both, and returns the handler
result. It contains no JSON parsing, Event Contract validation, chunk formation,
database access, persistence exception handling, or persistence logic.

Normal completion returns HTTP 200 with camel-case JSON containing `total`,
`accepted`, and derived `rejected`. This includes empty input and normally processed
input for which every record is rejected. Unsupported media types return HTTP 415.
Persistence or other unhandled failures are handled centrally as HTTP 500 Problem
Details. The safe response includes `HttpContext.TraceIdentifier` as `traceId` and
does not expose exception messages, stack traces, SQL details, or partial accounting.
The exception is logged, and previously committed chunks may remain durable. These
HTTP decisions are recorded in
[ADR 0007](../decisions/0007-expose-controller-based-ndjson-ingestion-api.md).

The implemented dependency graph and lifetimes are:

```text
HTTP
    ↓
EventsController
    ↓
NdjsonRecordReader (singleton)
    ↓
IngestEventsHandler (scoped)
    ├── EventEnvelopeValidator (singleton)
    └── IEventChunkStore (scoped)
            ↓
        EfCoreEventChunkStore
            ↓
        PulseFlowDbContext (scoped)
            ↓
        PostgreSQL
```

`PulseFlowDbContext` uses Npgsql with the required
`ConnectionStrings:PulseFlow` connection string. Deployments can supply it through
the `ConnectionStrings__PulseFlow` environment variable; no real credential is stored
in repository configuration. Application startup does not execute migrations.

`GlobalExceptionHandler` is registered through ASP.NET Core exception-handler
middleware with Problem Details. Status-code pages provide Problem Details for
otherwise body-less error statuses. First-party ASP.NET Core OpenAPI generation
documents the route, streaming NDJSON request body, and 200, 415, and 500 responses.
Swagger UI points to the generated document in Development only.

## Accepted Stage 2 target architecture (not implemented)

Stage 1 currently performs the complete ingestion pipeline during the HTTP request:

```text
HTTP -> parse -> validate -> PostgreSQL
```

For the many-client ingestion scenario, [ADR 0008](../decisions/0008-use-rabbitmq-to-decouple-http-ingestion-from-parsing.md)
accepts a different Stage 2 boundary. RabbitMQ buffers accepted batch work and
decouples HTTP ingestion throughput from the throughput of parsing and validation.
The accepted target is:

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

`PulseFlow.Api` remains the HTTP ingestion boundary. For the first Stage 2 slice, it
receives an NDJSON batch, performs only the request-level checks required before
acceptance, and publishes the complete raw NDJSON batch body to RabbitMQ. It does not
use external raw-batch storage or publish only a reference or identifier. It must not
parse individual NDJSON records or apply Event Contract v1 validation before
publishing the batch.

RabbitMQ publication confirmation is the Stage 2 HTTP acceptance boundary. The API
returns HTTP `202 Accepted` only after RabbitMQ has confirmed successful publication
of the batch; a publication failure must not return `202 Accepted`. The exact failure
response is not further defined by this decision and continues to use the existing
centralized error-handling boundary until a later implementation step needs more
specific behavior. The Stage 1 synchronous `200` result with `total`, `accepted`,
and `rejected` is therefore not the Stage 2 response contract, and the exact `202`
response body remains undecided. These decisions are recorded in
[ADR 0009](../decisions/0009-define-stage-2-rabbitmq-batch-acceptance-boundary.md).

`EventParserConsumer` is the concrete asynchronous component. It receives an NDJSON
batch from RabbitMQ, parses its records, applies the existing Event Contract v1
validation rules, and persists valid events to PostgreSQL through an appropriate
persistence boundary. The existing Stage 1 reader, validator, envelope, and
persistence logic are expected to be reused or repositioned where their contracts
remain suitable; moving the execution boundary alone is not a reason to rewrite them.
Multiple `EventParserConsumer` instances must be possible so parsing and validation
capacity can scale independently from `PulseFlow.Api`. This is not an artificial
business-processing worker and does not add a service merely to increase component
count.

This is an accepted target, not implemented architecture. The current API continues
to parse, validate, persist, and return synchronous Stage 1 accounting during the HTTP
request. RabbitMQ and `EventParserConsumer` do not yet exist in the repository.

## Not yet defined

- record, upload, and record-count limits;
- compression;
- authentication and authorization;
- idempotency, deduplication, and client retry behavior;
- request, batch, and RabbitMQ message-size limits;
- RabbitMQ exchange/queue topology, routing-key conventions, acknowledgement/requeue
  semantics, retry policy, dead-letter queues, and delivery guarantees;
- batch-status persistence and public status/query endpoint contract;
- deployment topology for RabbitMQ and parser consumers;
- a measured and tuned chunk capacity;
- retry and idempotency behavior for an overall failure after earlier chunks committed;
- PostgreSQL retry strategy, EF Core execution strategy, and transaction isolation level;
- production migration execution;
- the concrete validation library or framework.

RabbitMQ is accepted as the Stage 2 work-transfer broker. Redis is not part of the
implemented or Stage 2 target architecture; it is reserved for Stage 4 distributed
ingestion rate limiting across multiple `PulseFlow.Api` instances. Outbox, idempotency,
deduplication, and the detailed RabbitMQ reliability and topology choices remain
unresolved.
