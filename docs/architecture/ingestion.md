# Ingestion Architecture

**Status:** Stage 3 terminal rejection and event-level idempotency slices implemented

## Event record

Each event uses [Event Contract v2](../contracts/event-ingestion-v2.md). Its envelope
contains a source-owned UUID `eventId`, `type`, `source`, `occurredAt`, and an opaque
JSON-object `payload`. The logical identity is `(source, eventId)`; batch transport
does not change these event-level semantics.

The implemented NDJSON reader accepts a `Stream`, frames and parses records
incrementally, and exposes the ordered outcomes as `IAsyncEnumerable`. A record result
contains its one-based physical record number and either an independently owned
untrusted `JsonElement` or a malformed marker. It has no Event Contract, HTTP, or
persistence responsibility.

The implemented `EventEnvelope`/validation boundary accepts the parsed untrusted
`JsonElement`. It returns either an immutable valid `EventEnvelope` or
EventEnvelope-specific coded validation errors. `EventEnvelope` contains non-empty
`Guid` `EventId`, non-empty `Type` and `Source` strings, a UTC `DateTime` `OccurredAt`,
and an opaque object-valued `JsonElement` `Payload`. The payload is cloned while the envelope is constructed, so
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

Batch ingestion uses NDJSON with one independent Event Contract v2 record per line. A completely received record is a separate unit of ingestion:

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
Event Contract v2 validation
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
determines whether it satisfies Event Contract v2: UUID `eventId`, `type`, `source`,
`occurredAt`, and the requirement that `payload` is a JSON object. It does not serialize and parse the
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
not a claim of request-level atomicity. A later replay with the same `(source, eventId)`
does not duplicate the earlier committed logical events.

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
cancellation. Its PostgreSQL implementation sends the complete supplied collection as
one parameterized `INSERT ... VALUES ... ON CONFLICT (source, event_id) DO NOTHING`
command inside one explicit transaction. Successful completion is the durability
boundary for that supplied chunk; cancellation and persistence failures propagate.
The unique index is the concurrency correctness boundary, so duplicates are successful
no-ops rather than a select-before-insert decision or duplicate-key exception path.

The direct translation is:

```text
EventRecord.Id          <- Guid.NewGuid()
EventRecord.EventId     <- EventEnvelope.EventId
EventRecord.Type        <- EventEnvelope.Type
EventRecord.Source      <- EventEnvelope.Source
EventRecord.OccurredAt  <- EventEnvelope.OccurredAt
EventRecord.ReceivedAt  <- DateTime.UtcNow
EventRecord.PayloadJson <- EventEnvelope.Payload JSON
```

`EventRecord.Id` remains the server-generated primary key of one durable row. The
database also has a unique `(source, event_id)` index. The payload is passed to the existing PostgreSQL `jsonb` mapping, which preserves its
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

Application composition calls `builder.Services.AddIngestionMessaging(builder.Configuration)`.
The extension validates RabbitMQ options and keeps RabbitMQ.Client primitives inside
`Ingestion/Messaging/RabbitMq`. `RabbitMqConnectionManager` owns one long-lived
application connection. During hosted-service startup it establishes that connection,
declares the configured durable main queue, and declares a durable direct dead-letter
exchange and durable dead-letter queue. The main queue uses an explicit dead-letter
routing key. Startup fails if that topology cannot be declared.

The main queue has application-owned `x-dead-letter-exchange` and
`x-dead-letter-routing-key` arguments. RabbitMQ does not allow changing those
arguments on an existing queue. The application never deletes, purges, or silently
recreates a queue, so an existing local Stage 2 queue must be manually recreated before
this topology can be used. Mutable production DLX configuration through RabbitMQ
policies is not automated in this project slice.
The manager creates the publisher and consumer channels and disposes the connection
after its dependent messaging resources have stopped.

The singleton `RabbitMqIngestionBatchPublisher` owns a publisher-confirmation-enabled
channel. It serializes access with `SemaphoreSlim`, sets persistent
`application/x-ndjson` message properties, and publishes the exact raw body to the
configured queue through RabbitMQ's default exchange. The RabbitMQ client's automatic
recovery remains at its default enabled setting; this cleanup adds no custom retry or
reconnection behavior. A failed publication still faults the publisher task and reaches
the HTTP exception boundary. This is the local, minimum Step 2 topology and lifecycle
choice rather than a final topology or delivery guarantee.

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
greater than zero. It controls the number of competing parser workers inside one
application process; it does not control or imply the number of HTTP API instances.
The existing PostgreSQL composition and reusable Stage 1 parsing, validation, and
persistence services are not in the HTTP request path. They are resolved and used by
the parser consumer per RabbitMQ delivery.

`GlobalExceptionHandler` is registered through ASP.NET Core exception-handler
middleware with Problem Details. Status-code pages provide Problem Details for
otherwise body-less error statuses. First-party ASP.NET Core OpenAPI generation
documents the route, streaming NDJSON request body, and 202, 415, and 500 responses.
Swagger UI points to the generated document in Development only.

## Parser consumer and terminal failure handling

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
Event Contract v2 validation
    ->
PostgreSQL
```

This implements Steps 2 and 3 of the boundary accepted by
[ADR 0008](../decisions/0008-use-rabbitmq-to-decouple-http-ingestion-from-parsing.md)
and [ADR 0009](../decisions/0009-define-stage-2-rabbitmq-batch-acceptance-boundary.md).

`EventParserConsumer` is one ASP.NET Core hosted `BackgroundService`. It starts the
configured number of workers. Each worker uses `IIngestionBatchConsumerFactory` to
create an independent `IIngestionBatchConsumer`; the RabbitMQ implementation creates
one long-lived consumer channel from the application's long-lived connection. It does
not share the publisher channel. The adapter consumes the configured durable queue with
manual acknowledgement and copies the RabbitMQ body before processing because
RabbitMQ.Client only guarantees the delivered memory during the callback.

The hosted service starts exactly `RabbitMq:ConsumerCount` workers, gives every worker
the host stopping token, and awaits all worker tasks with `Task.WhenAll`. It does not
coordinate worker failures, cancel siblings, restart workers, or add health or recovery
behavior. A worker exception naturally faults the `BackgroundService` after all worker
tasks complete. A normally completed worker is not treated as a special failure. This
lifecycle is recorded in
[ADR 0013](../decisions/0013-use-backgroundservice-worker-lifecycle.md).

The application-facing consumer boundary is callback-based:
`IIngestionBatchConsumer.ConsumeAsync` receives a handler for each
`IngestionBatchDelivery`. `IngestionBatchDelivery` exposes only the raw batch body,
an acknowledgement method, and a terminal rejection method. RabbitMQ's callback
model, delivery tag, `IConnection`, `IChannel`, and basic consume and acknowledgement
calls remain inside the RabbitMQ adapter. `RabbitMqIngestionBatchConsumer` copies the
delivery body and invokes the application handler directly; it does not introduce an
application-level queue or buffer. Broker-side flow control through RabbitMQ prefetch
is intentionally deferred.

For each delivery, the consumer creates an asynchronous DI scope, resolves
the scoped `IngestEventsHandler` and its EF Core persistence dependencies from that
scope, exposes the copied raw batch through a fresh `MemoryStream`, and passes it to a
fresh `NdjsonRecordReader` sequence. The handler retains the established Stage 1
validation, chunking, and persistence semantics. The consumer acknowledges the
delivery only after the handler completes successfully.

Any processing failure logs the failure and terminally rejects that delivery. The
RabbitMQ adapter performs this as a single-delivery reject with `requeue = false`,
which routes the batch to the configured dead-letter queue. The consumer does not make
an in-process PostgreSQL or batch retry. There are no retry queues, `requeue = true`,
or automatic redrive operations. This does not provide exactly-once processing.

Malformed NDJSON records and contract-invalid records remain normal record-level
outcomes inside the existing handler. They do not cause the complete RabbitMQ batch to
be dead-lettered. Shutdown cancellation is passed to RabbitMQ consumption where
supported, the reader, and the handler; it is not logged as a processing failure and
does not reject a delivery. On hosted-service shutdown, consumption is cancelled and
the consumer-owned channel is disposed before application composition disposes the
shared RabbitMQ connection. If a consumer tag exists, the adapter attempts
`BasicCancelAsync` and always attempts channel disposal even when that cancellation
call fails. It does not explicitly settle outstanding deliveries during cleanup.

The dead-letter queue preserves a failed raw batch for investigation, but it does not
provide an end-to-end no-loss, at-least-once, or exactly-once guarantee. RabbitMQ
dead-letter republishing can fail depending on broker topology and availability. Earlier
PostgreSQL chunks can also have committed before a later chunk fails. Reprocessing a
batch with the same Contract v2 event identities does not duplicate committed logical
events because PostgreSQL treats them as no-ops. The decision and its limits are
recorded in [ADR 0012](../decisions/0012-process-each-rabbitmq-delivery-once.md)
and [ADR 0011](../decisions/0011-use-event-level-idempotency.md).

Prefetch must be designed after this processing-failure acknowledgement behavior has
been verified. This slice does not add broker-side flow control. Retry queues,
dead-letter redrive, poison-message handling beyond terminal broker retention, Outbox,
and delivery-guarantee policies remain unresolved.

The single hosted service starts one worker for each validated `RabbitMq:ConsumerCount`
value. Each worker owns an independent consumer channel, while all consumer channels
and the publisher channel share the one application RabbitMQ connection. The consumers
subscribe to the same configured queue, and RabbitMQ is responsible for distributing
deliveries between these competing consumers. This is structural capacity configuration
only; it does not establish throughput, fairness, or load-performance results.

The complete path is verified in an integration test using real RabbitMQ and PostgreSQL
Testcontainers. The test starts both containers, starts the real application with a
generated isolated queue name, posts `application/x-ndjson` to `POST /api/events`,
asserts HTTP 202, and polls PostgreSQL with a bounded 15-second timeout until the
expected rows appear. A focused real-broker test also verifies that `ConsumerCount = 2`
registers two consumers on the configured queue by polling RabbitMQ queue metadata.
These tests do not claim an ordering, distribution, or performance characteristic.

## Distributed ingestion rate limiting

Stage 4 has an accepted Redis-backed global quota design. All `PulseFlow.Api`
instances will share one quota, using a fixed window. The limiter must run before the
request body is read and before a batch is published to RabbitMQ. A quota that is
already exhausted will result in HTTP 429; Redis unavailability will result in HTTP
503. There is no process-local fallback counter. See
[ADR 0014](../decisions/0014-use-redis-for-global-ingestion-rate-limiting.md).

`ConnectionStrings:Redis` supplies the connection, and the process owns one shared
`IConnectionMultiplexer`. `IngestionRateLimit:RequestLimit` and
`IngestionRateLimit:WindowDuration` are required positive startup-validated settings.
`RedisIngestionRateLimiter` is registered behind the Redis-independent
`IIngestionRateLimiter` contract. It evaluates one atomic Lua script against
`pulseflow:rate-limit:ingestion:global`: the script denies an exhausted counter, or
increments the counter and applies its fixed-window TTL only when it creates the
counter. It returns the decision and remaining TTL. The limiter maps an allowed result,
an exceeded result with `RetryAfter`, or an unavailable result when the Redis operation
fails. There is no retry, lock, cache, or local fallback counter.

No HTTP endpoint uses the limiter contract yet.

## Not yet defined

- record, upload, and record-count limits;
- compression;
- authentication and authorization;
- automatic dead-letter redrive and a client retry policy beyond preserving `eventId`;
- request, batch, and RabbitMQ message-size limits;
- RabbitMQ routing-key conventions beyond the direct configured-queue publish;
- acknowledgement/requeue semantics beyond successful manual acknowledgement, retry
  policy, dead-letter queues, poison-message handling, and delivery guarantees;
- broker-side flow control and RabbitMQ prefetch, which must follow the processing-
  failure acknowledgement decision;
- batch-status persistence and public status/query endpoint contract;
- deployment topology for RabbitMQ and parser consumers;
- a measured and tuned chunk capacity;
- retry behavior for an overall failure after earlier chunks committed;
- PostgreSQL retry strategy, EF Core execution strategy, and transaction isolation level;
- production migration execution;
- the concrete validation library or framework.

HTTP `Retry-After` serialization, exact rate-limit values, client identity, and the
load-test scenario remain undefined. Outbox and detailed RabbitMQ reliability choices
remain unresolved.
