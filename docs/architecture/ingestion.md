# Ingestion Architecture

**Status:** Stage 2 API-to-RabbitMQ acceptance boundary, parser consumer, real-broker verification, and parser-consumer capacity configuration implemented; review remains

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
chunks, and returns normal-completion accounting. These retained Stage 1 components
are no longer in the HTTP request path; `EventParserConsumer` now reuses them for
RabbitMQ deliveries. The earlier Stage 1 HTTP-to-PostgreSQL verification remains
historical evidence of their behavior, not the behavior of the current endpoint.

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
controller and restricted to `application/x-ndjson`. The controller copies the
complete request body as raw bytes and passes those bytes to
`IIngestionBatchPublisher`; it does not invoke `NdjsonRecordReader`,
`EventEnvelopeValidator`, `IngestEventsHandler`, or PostgreSQL persistence.

Application composition validates the RabbitMQ options and creates one long-lived
RabbitMQ connection and one publisher-confirmation-enabled channel during startup. It
declares the configured durable named queue before the host starts accepting requests,
registers the ready channel for application use, and disposes the channel and
connection after the host stops. Startup fails if these RabbitMQ resources or the
queue declaration cannot be initialized.

The singleton `RabbitMqIngestionBatchPublisher` receives the ready shared channel and
owns only batch publication. It serializes channel access with `SemaphoreSlim`, sets
persistent `application/x-ndjson` message properties, and publishes the exact raw
body to the configured queue through RabbitMQ's default exchange. The RabbitMQ
client's automatic recovery remains at its default enabled setting; this cleanup adds
no custom retry or reconnection behavior. A failed publication still faults the
publisher task and reaches the HTTP exception boundary. This is the local, minimum
Step 2 topology and lifecycle choice rather than a final topology or delivery
guarantee.

HTTP `202 Accepted` with no body is returned only after that publisher task completes
successfully. Consequently, it means RabbitMQ confirmed publication of the raw batch,
not that any record was parsed, validated, or persisted. Unsupported media types
return HTTP 415. Publisher and other unhandled failures continue to use the existing
centralized HTTP 500 Problem Details boundary, with no `202` response.

The implemented dependency graph and lifetimes are:

```text
HTTP
    ↓
EventsController
    ↓
IIngestionBatchPublisher (singleton)
    ↓
RabbitMqIngestionBatchPublisher
    ↓
RabbitMQ
```

`RabbitMqOptions` validates the required `RabbitMq:QueueName` on startup. The required
RabbitMQ connection string is `ConnectionStrings:RabbitMq`, which deployments can
override using `ConnectionStrings__RabbitMq`; no real credential is stored in
repository configuration. `RabbitMq:ConsumerCount` defaults to 1 and is validated as
greater than zero. It controls the number of competing `EventParserConsumer` instances
inside one application process; it does not control or imply the number of HTTP API
instances. The existing PostgreSQL composition and reusable Stage 1 parsing,
validation, and persistence services are not in the HTTP request path. They are
resolved and used by the parser consumer per RabbitMQ delivery.

`GlobalExceptionHandler` is registered through ASP.NET Core exception-handler
middleware with Problem Details. Status-code pages provide Problem Details for
otherwise body-less error statuses. First-party ASP.NET Core OpenAPI generation
documents the route, streaming NDJSON request body, and 202, 415, and 500 responses.
Swagger UI points to the generated document in Development only.

## Stage 2 parser consumer

The implemented asynchronous path is:

```text
Client
    ↓
PulseFlow.Api
    ↓
RabbitMQ
    ->
EventParserConsumer
    ->
NDJSON parsing
    ->
Event Contract v1 validation
    ->
PostgreSQL
```

This implements Steps 2 and 3 of the boundary accepted by
[ADR 0008](../decisions/0008-use-rabbitmq-to-decouple-http-ingestion-from-parsing.md)
and [ADR 0009](../decisions/0009-define-stage-2-rabbitmq-batch-acceptance-boundary.md).

`EventParserConsumer` is an ASP.NET Core hosted `BackgroundService`. It receives
deliveries through its own long-lived RabbitMQ channel created from the application's
long-lived connection; it does not share the publisher channel. The channel consumes
the configured durable queue with manual acknowledgement and copies the RabbitMQ body
before processing because RabbitMQ.Client only guarantees the delivered memory during
the callback.

For each delivery, the consumer creates an asynchronous DI scope, resolves the scoped
`IngestEventsHandler` and its EF Core persistence dependencies from that scope, exposes
the copied raw batch through a `MemoryStream`, and passes it to the singleton
`NdjsonRecordReader`. The handler retains the established Stage 1 validation,
chunking, and persistence semantics. The consumer acknowledges the delivery only
after the handler completes successfully.

If normal processing fails, the consumer logs the exception and deliberately sends no
successful acknowledgement, reject, or negative acknowledgement. The delivery stays
unacknowledged for the current channel. This is a deliberately minimal Step 3 failure
behavior, not a retry, requeue, dead-letter, poison-message, or delivery-guarantee
policy. Shutdown cancellation is passed to RabbitMQ consumption where supported, the
reader, and the handler; it is not logged as a processing failure. On hosted-service
shutdown, consumption is cancelled and the consumer-owned channel is disposed before
application composition disposes the shared RabbitMQ connection.

Application composition registers one `EventParserConsumer` for each validated
`RabbitMq:ConsumerCount` value. Each instance creates and owns an independent consumer
channel, while all consumer channels and the publisher channel share the one application
RabbitMQ connection. The consumers subscribe to the same configured queue, and RabbitMQ
is responsible for distributing deliveries between these competing consumers. This is
structural capacity configuration only; it does not establish throughput, fairness, or
load-performance results.

The complete path is verified in an integration test using real RabbitMQ and PostgreSQL
Testcontainers. The test starts both containers, starts the real application with a
generated isolated queue name, posts `application/x-ndjson` to `POST /api/events`,
asserts HTTP 202, and polls PostgreSQL with a bounded 15-second timeout until the
expected rows appear. A focused real-broker test also verifies that `ConsumerCount = 2`
starts two parser consumers that use the same configured queue and connection but
different consumer channels. These tests do not claim an ordering, distribution, or
performance characteristic.

## Not yet defined

- record, upload, and record-count limits;
- compression;
- authentication and authorization;
- idempotency, deduplication, and client retry behavior;
- request, batch, and RabbitMQ message-size limits;
- RabbitMQ routing-key conventions beyond the direct configured-queue publish;
- acknowledgement/requeue semantics beyond successful manual acknowledgement, retry
  policy, dead-letter queues, poison-message handling, and delivery guarantees;
- batch-status persistence and public status/query endpoint contract;
- deployment topology for RabbitMQ and parser consumers;
- a measured and tuned chunk capacity;
- retry and idempotency behavior for an overall failure after earlier chunks committed;
- PostgreSQL retry strategy, EF Core execution strategy, and transaction isolation level;
- production migration execution;
- the concrete validation library or framework.

Redis is not part of this implementation; it is reserved for Stage 4 distributed
ingestion rate limiting across multiple `PulseFlow.Api` instances. Outbox,
idempotency, deduplication, and detailed RabbitMQ reliability choices remain
unresolved.
