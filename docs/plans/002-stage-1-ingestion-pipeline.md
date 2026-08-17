# PLAN 002: Stage 1 Ingestion Pipeline

**Document type:** PLAN

**Status:** In progress; Steps 1 through 5 implemented and verified, with the
focused Step 6 end-to-end test foundation in progress

**ADR status:** This document is not an ADR. It sequences implementation of the design accepted in [ADR 0001](../decisions/0001-use-ndjson-for-batch-ingestion.md), [ADR 0002](../decisions/0002-use-chunked-postgresql-persistence-for-ingestion.md), and [ADR 0003](../decisions/0003-construct-event-envelope-after-contract-validation.md); it does not replace those decisions or describe a fully implemented pipeline.
Exact stream framing for Step 2 is accepted in
[ADR 0004](../decisions/0004-define-ndjson-record-framing.md). Persistence metadata
generation for Step 3 is accepted in
[ADR 0005](../decisions/0005-generate-event-persistence-metadata-in-application.md).
The simple orchestration result and persistence-failure propagation for Step 4 are
accepted in
[ADR 0006](../decisions/0006-keep-ingestion-handler-failure-propagation-simple.md).
The controller-based HTTP contract and centralized failure response for Step 5 are
accepted in
[ADR 0007](../decisions/0007-expose-controller-based-ndjson-ingestion-api.md).

## Purpose

Define the incremental implementation sequence that connects the completed [Stage 1 persistence foundation](001-stage-1-persistence-foundation.md) to the Stage 1 NDJSON ingestion use case.

The completed foundation already provides `EventRecord`, `PulseFlowDbContext`, explicit EF Core mapping, a committed migration, and focused persistence tests against real PostgreSQL. This plan starts at that boundary. It does not reopen PLAN 001 or claim that request streaming, validation, orchestration, application database wiring, or an ingestion endpoint currently exists.

The target logical flow is:

```text
HTTP request stream
    ↓
NDJSON record reader
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
later ingestion orchestration and chunk formation
    ↓
IEventChunkStore
    ↓
PostgreSQL commit
```

The complete flow above remains a target sequence. Steps 1 and 2 implement the
`EventEnvelope`/validation boundary and the NDJSON reader/JSON syntax boundary. Step
3 implements the durable chunk-store boundary and its EF Core translation to
`EventRecord`. Step 4 implements orchestration, chunk formation, and normal-completion
accounting. Step 5 implements the controller HTTP boundary, application persistence
wiring, configuration validation, centralized error handling, and Development
OpenAPI/Swagger UI. End-to-end HTTP-to-PostgreSQL verification remains unimplemented.

## Planning constraints

- Keep the implementation inside `PulseFlow.Api` for now. Use cohesive folders and namespaces, but do not create Domain, Application, or Infrastructure assemblies.
- Organize contract reading, validation, orchestration, and persistence responsibilities so that real boundaries can be extracted later without making extraction a goal of Stage 1.
- Keep `EventEnvelope`, the ingestion-contract representation, separate from `EventRecord`, the EF Core persistence representation. Do not bind HTTP input directly to the EF entity and do not add persistence-only fields to Event Contract v1.
- Read one NDJSON record at a time with asynchronous streaming or iterator semantics. Buffering one bounded record and one configurable persistence chunk is compatible with the accepted design; buffering the complete upload is not.
- Parse and validate every completely received record independently. A malformed or invalid record is rejected and excluded from persistence, but it does not reject other completely received valid records.
- Make `IngestEventsHandler` responsible for ingestion orchestration and accounting. Keep HTTP types and status-code decisions out of it, and keep `DbContext`, EF Core, transactions, and entity-state operations out of it.
- Introduce `IEventChunkStore` as the handler's durable persistence boundary when that boundary is needed for the handler slice. Do not generalize it into `Repository<T>`, `UnitOfWork`, a base repository, or a speculative application-layer framework.
- Make the EF Core implementation of `IEventChunkStore` translate ingestion data to the separate `EventRecord` representation and persist exactly one supplied chunk using the existing `PulseFlowDbContext`.
- Count a record as accepted only after the PostgreSQL operation for its chunk has committed successfully. Successful validation or placement in an in-memory chunk is not acceptance. If a chunk fails, none of its records are accepted; records from earlier committed chunks remain accepted.
- Use explicit constructors inside C# type bodies. Do not use C# primary constructors.

## Incremental implementation order

Each step is intentionally bounded. Its explicit non-goals are part of the step and prevent implementation of later concerns before the preceding boundary has been proved.

### Step 1: Add `EventEnvelope` and focused record validation

**Implementation status:** Completed on 2026-08-16. See the
[Step 1 checkpoint](../progress/2026-08-16-012-event-envelope-validation-boundary.md).

#### Responsibility introduced

Inspect one already-parsed, untrusted JSON value and validate only the accepted
record-level rules. Construct `EventEnvelope` only after every rule succeeds:

- `type` is present, is a string, and is non-empty;
- `source` is present, is a string, and is non-empty;
- `occurredAt` is present and is an RFC 3339 UTC timestamp serialized with `Z`;
- `payload` is present and its top-level JSON value is an object;
- the internal contents of `payload` remain opaque.

The untrusted parsed representation must retain enough information for validation to
distinguish missing properties, JSON `null`, incorrect JSON types, and a timestamp that
is not expressed with the required UTC `Z` form. The validator accepts that already
parsed representation and must not serialize and parse it again. A successfully
constructed `EventEnvelope` must own any retained JSON data rather than borrowing it
from a parser document that the caller can dispose.

#### Conceptual files and types

- `src/PulseFlow.Api/Ingestion/Contracts/EventEnvelope.cs`
- `src/PulseFlow.Api/Ingestion/Validation/EventEnvelopeValidator.cs`
- a small validation result/error model if one is required to report record-level reasons without HTTP concepts
- `tests/PulseFlow.UnitTests/Ingestion/Validation/EventEnvelopeValidatorTests.cs`

Exact filenames may follow the repository's conventions when implemented. A third-party validation framework is not required for these focused rules and must not be added without a demonstrated need.

#### What this step proves

- The ingestion contract has a representation that is distinct from `EventRecord`.
- `EventEnvelope` exists only for a record that has passed Event Contract v1 validation.
- Each rule already accepted by [Event Contract v1](../contracts/event-ingestion-v1.md) has focused positive and negative tests.
- Arbitrary nested object payloads are accepted without sender-specific payload models or interpretation.
- Validation results are usable by later orchestration without depending on ASP.NET Core HTTP response types or EF Core.

#### Explicit non-goals

- Do not read a request stream or split NDJSON records.
- Do not parse JSON text, parse multiple records, or recover after malformed JSON.
- Do not create `IEventChunkStore`, map to `EventRecord`, or access PostgreSQL.
- Do not create `IngestEventsHandler`, form chunks, or define accepted/rejected batch totals.
- Do not add an endpoint, application DI registration, options binding, or OpenAPI changes.
- Do not decide identifier generation, `received_at` assignment, unknown-property behavior, maximum field lengths, record-size limits, or upload limits.

### Step 2: Add the asynchronous NDJSON record reader and parser tests

**Implementation status:** Completed on 2026-08-17. See the
[Step 2 checkpoint](../progress/2026-08-17-013-ndjson-record-reader.md).

#### Responsibility introduced

Read the supplied stream incrementally, establish NDJSON record boundaries, and parse
each completely received record once into an untrusted JSON representation. Yield a
record result asynchronously. A syntax parse failure must be represented as a result
for that record so iteration can continue with later independent records when the
stream remains readable. Contract validation of a successfully parsed representation
constructs `EventEnvelope` at the boundary established by Step 1.

The reader owns framing and JSON syntax concerns. It does not decide contract validity,
construct `EventEnvelope`, persist records, or accumulate the complete upload. Its
yielded result should preserve record order and enough location information, such as a
one-based record number, for later accounting and diagnosis without embedding an HTTP
response model.

[ADR 0004](../decisions/0004-define-ndjson-record-framing.md) defines the framing
behavior implemented here: LF and CRLF terminate records, blank lines are malformed
empty records, and a syntactically complete final JSON value is accepted at clean
end-of-stream without a trailing newline. Incomplete or malformed final JSON is
reported as malformed and is never repaired heuristically. Record numbers are
one-based in physical stream order, while cancellation propagates rather than becoming
a malformed-record result.

#### Conceptual files and types

- `src/PulseFlow.Api/Ingestion/Ndjson/NdjsonRecordReader.cs`
- a per-record read result that distinguishes parsed untrusted JSON from a malformed record
- focused parser or serializer support colocated with the NDJSON responsibility if separation makes the tests clearer
- `tests/PulseFlow.UnitTests/Ingestion/Ndjson/NdjsonRecordReaderTests.cs`

The public surface should expose asynchronous iteration, such as `IAsyncEnumerable<T>`, and accept cancellation. It should not return a materialized collection for the upload.

#### What this step proves

- Records are yielded in order before the complete upload has been buffered.
- Valid representative records are parsed once into untrusted JSON values that the
  Step 1 validator can turn into `EventEnvelope` values, with opaque nested payloads
  intact.
- A malformed record produces a record-level parse failure and does not prevent a later valid record from being yielded.
- Clean termination, malformed final input, newline variants, cancellation, and the selected blank-line behavior are deterministic and tested.
- The reader buffers at most its current record; record-size protection remains a later decision.

#### Explicit non-goals

- Do not duplicate Event Contract v1 validation inside the reader.
- Do not create chunks, accepted/rejected aggregate accounting, or ingestion orchestration.
- Do not introduce `IEventChunkStore`, EF Core access, transactions, or PostgreSQL tests for the reader.
- Do not accept `HttpRequest`, return `IResult`, choose a route or status code, or register services.
- Do not add upload/record-size limits, compression, authentication, retry, idempotency, or malformed-JSON repair.

### Step 3: Add `IEventChunkStore` and its EF Core implementation

**Implementation status:** Completed on 2026-08-17. See the
[Step 3 checkpoint](../progress/2026-08-17-014-event-chunk-store.md).

#### Responsibility introduced

Create the narrow durable boundary needed by the next handler step. `IEventChunkStore` persists one already-formed chunk and completes successfully only after the PostgreSQL operation for that chunk commits. The EF Core implementation uses the existing `PulseFlowDbContext`, maps ingestion data to new `EventRecord` instances, and performs one chunk persistence operation.

The store contract must not expose `DbContext`, EF Core entity-state APIs, or transaction objects to the handler. It also must not present a successful return before the commit that makes the chunk durable. Cancellation and persistence failures propagate through this boundary; retry policy and HTTP interpretation do not belong here.

The implemented mapping follows
[ADR 0005](../decisions/0005-generate-event-persistence-metadata-in-application.md):
the application assigns persistence-only IDs with `Guid.NewGuid()` and receipt times
with `DateTime.UtcNow` directly during translation. No clock, ID generator, mapper, or
factory abstraction is introduced. This does not change Event Contract v1.

#### Conceptual files and types

- `src/PulseFlow.Api/Ingestion/Persistence/IEventChunkStore.cs`, owned near its handler consumer
- `src/PulseFlow.Api/Persistence/Events/EfCoreEventChunkStore.cs`
- focused unit tests for mapping only if useful
- focused integration tests in `PulseFlow.IntegrationTests` using the existing PostgreSQL Testcontainers fixture and committed migration

The interface should accept the narrow ingestion data needed to persist a chunk, not `EventRecord` values created by the HTTP layer. The EF implementation is responsible for the translation to the persistence representation.

#### What this step proves

- The future handler can depend on one use-case-specific durability boundary without depending on EF Core.
- One supplied chunk is translated to distinct `EventRecord` instances and persisted through the existing mapping to real PostgreSQL.
- A successful store completion means the chunk has committed and can be counted as accepted.
- The interface and implementation do not become a generic repository or a second unit-of-work abstraction over `PulseFlowDbContext`.

#### Explicit non-goals

- Do not create `IngestEventsHandler` or let the store choose chunk boundaries.
- Do not read NDJSON, validate envelopes, count rejected records, or depend on HTTP types.
- Do not register the store or `PulseFlowDbContext` in application DI yet.
- Do not choose the operational chunk size.
- Do not add database retries, an EF Core execution strategy, a transaction-isolation policy, an Outbox, idempotency, deduplication, or a production migration runner.
- Do not create `Repository<T>`, `UnitOfWork`, base persistence abstractions, or new assemblies.

### Step 4: Add `IngestEventsHandler`, configurable chunk formation, and accounting

**Implementation status:** Completed on 2026-08-17. See the
[Step 4 checkpoint](../progress/2026-08-17-015-ingestion-handler.md).

#### Responsibility introduced

Orchestrate ingestion independently of HTTP and EF Core. The handler consumes the asynchronous sequence of per-record reader results, applies record validation to parsed envelopes, excludes malformed and invalid records from persistence, forms bounded chunks of valid records, and passes each full or final partial chunk to `IEventChunkStore` in order.

The handler increments total for every processed input record and increments accepted only after the corresponding store call succeeds. On normal completion, rejected is derived as `Total - Accepted`; it is not maintained as a separate counter. If persistence fails, records in the failing chunk are not accepted, earlier committed chunks remain durable, the original exception propagates, and no handler result is returned, so valid-but-uncommitted records are not classified by the normal-completion formula.

Chunk capacity must be supplied as validated configuration to the handler. Tests should use deliberately small values to prove multiple chunks and final-chunk flushing; those values are test inputs and do not accept a concrete operational chunk size. The production configuration source and value are wired in Step 5.

This simple failure behavior is a deliberate trade-off recorded in [ADR 0006](../decisions/0006-keep-ingestion-handler-failure-propagation-simple.md). It does not decide how the later HTTP layer represents a failure after earlier chunks have committed.

#### Conceptual files and types

- `src/PulseFlow.Api/Ingestion/IngestEventsHandler.cs`
- `src/PulseFlow.Api/Ingestion/IngestEventsResult.cs`
- `tests/PulseFlow.UnitTests/Ingestion/IngestEventsHandlerTests.cs`
- a small fake or recording `IEventChunkStore` owned by the unit tests

#### What this step proves

- The handler processes the stream in order while retaining at most the current record and current chunk rather than the complete upload.
- Malformed and invalid records contribute to the derived rejected total, and later valid records remain eligible for persistence.
- Only valid records enter store calls; full chunks and the final partial chunk have the expected ordered contents for several configured test capacities.
- Accepted totals advance only after successful store completion.
- On a later store failure, the persistence exception propagates, earlier committed chunks remain durable, processing stops, and no partial handler result is invented.
- The handler has no dependency on `HttpContext`, endpoint response types, `PulseFlowDbContext`, or EF Core.

#### Explicit non-goals

- Do not add an HTTP endpoint, choose response status codes, or turn the handler result into a public response contract.
- Do not bind configuration from `appsettings` or register services in application DI.
- Do not make the handler construct a `DbContext`, manage EF transactions, run migrations, or implement retries.
- Do not select a production chunk size or infer load-performance claims from unit tests.
- Do not add parallel record processing, parallel chunk writes, idempotency, deduplication, messaging, Redis, RabbitMQ, or Outbox behavior.

### Step 5: Add the HTTP endpoint and application wiring

**Implementation status:** Completed on 2026-08-17. See the
[Step 5 checkpoint](../progress/2026-08-17-016-http-ingestion-boundary.md).

#### Responsibility introduced

Expose the Stage 1 HTTP ingestion surface and compose the previously proved responsibilities. The endpoint supplies the request body and request cancellation to the NDJSON reader, passes the resulting asynchronous record sequence into `IngestEventsHandler`, and maps the application outcome to the accepted HTTP contract. It contains no record-validation rules, chunking algorithm, or EF Core persistence logic.

Register the reader, validator, handler, `IEventChunkStore`, and existing `PulseFlowDbContext` with lifetimes compatible with streaming one request and using one context-backed store at a time. Bind and validate the PostgreSQL connection string and chunk-capacity configuration. Configure the Npgsql EF Core provider for application execution.

The accepted contract is `POST /api/events` consuming `application/x-ndjson`. Normal completion, including empty input or a result with only rejected records, returns HTTP 200 with `{ total, accepted, rejected }`. Unsupported media types return 415. Persistence and other unhandled failures become safe HTTP 500 Problem Details through centralized exception handling; the response contains no partial accounting even though earlier chunks may remain durable. See [ADR 0007](../decisions/0007-expose-controller-based-ndjson-ingestion-api.md).

Application startup must not silently establish a production migration execution strategy. For Stage 1, the application may require a separately migrated database while integration tests apply committed migrations as test setup. Any automatic startup migration behavior requires an explicit decision rather than being inferred from `AddDbContext` wiring.

#### Conceptual files and types

- `src/PulseFlow.Api/Ingestion/Http/EventsController.cs`
- `src/PulseFlow.Api/Http/GlobalExceptionHandler.cs`
- `src/PulseFlow.Api/Program.cs`
- `src/PulseFlow.Api/Ingestion/IngestionOptions.cs`
- PostgreSQL connection-string and ingestion configuration keys in the appropriate configuration files, without committing secrets
- first-party OpenAPI generation and Development-only Swagger UI

#### What this step proves

- The application starts with explicit configuration and resolves the complete ingestion dependency graph.
- The endpoint is thin and delegates the stream, cancellation, and orchestration to the appropriate components.
- The application uses the existing `PulseFlowDbContext` with Npgsql through `IEventChunkStore`.
- Invalid configuration fails predictably rather than creating zero-sized or unbounded chunk behavior.
- The implemented route, media type, response model, and OpenAPI description agree with the accepted Stage 1 HTTP contract.

#### Explicit non-goals

- Do not add a public retrieval endpoint solely to verify ingestion.
- Do not move types into new Domain, Application, or Infrastructure assemblies.
- Do not add authentication, authorization, compression, upload limits, record-size limits, retry, idempotency, deduplication, RabbitMQ, Redis, or an Outbox.
- Do not choose or automate the production migration execution strategy.
- Do not claim end-to-end PostgreSQL behavior until Step 6 passes.

### Step 6: Add end-to-end integration tests against real PostgreSQL

**Implementation status:** In progress. The reusable per-test HTTP and PostgreSQL
setup, successful-response accounting scenario, committed-row scenario, mixed-input
accounting, and valid persistence across a full plus final partial chunk are
implemented and verified. The deterministic later-chunk persistence-failure scenario
and other remaining Step 6 scenarios are intentionally pending.

#### Responsibility introduced

Verify the complete HTTP-to-PostgreSQL path using the running ASP.NET Core application and a real PostgreSQL Testcontainers instance. The test host supplies its generated connection string, applies the committed migrations as controlled test setup, and overrides chunk capacity with a small test value that forces more than one chunk. Persistence is verified directly through PostgreSQL; no query endpoint is added for test convenience.

The representative scenarios should include:

- a valid multi-record NDJSON request whose rows and opaque nested payloads are persisted;
- a mixed request containing valid, malformed, and contract-invalid records, followed by another valid record, proving independent rejection and continued processing;
- enough valid records to prove a committed full chunk and a committed final partial chunk;
- normal-completion response accounting that matches the rows committed in PostgreSQL;
- a deterministic mid-request PostgreSQL failure scenario, once the Step 5 HTTP behavior is accepted, proving that earlier committed chunks remain durable and the failing chunk is not reported as accepted;
- application cancellation or truncated-final-record behavior where it can be made deterministic and useful without introducing retry or idempotency semantics.

#### Conceptual files and types

- an ASP.NET Core integration-test factory or fixture under `tests/PulseFlow.IntegrationTests/Infrastructure/`
- Stage 1 ingestion endpoint tests under `tests/PulseFlow.IntegrationTests/Ingestion/`
- reuse or careful extension of the existing `PostgreSqlFixture`; do not create a competing database lifecycle without need

#### What this step proves

- A client can stream Stage 1 NDJSON to the real application and committed valid records can be observed in real PostgreSQL.
- `EventEnvelope` remains distinct from `EventRecord` while all accepted contract fields survive the complete mapping and persistence path.
- One malformed or invalid record does not reject other completely received valid records.
- Actual persisted rows and normal-completion accepted/rejected accounting respect the PostgreSQL commit boundary; failure behavior matches the HTTP decision accepted in Step 5.
- The clean test environment reaches the intended schema through the repository's committed migrations.

#### Explicit non-goals

- Do not use SQLite, the EF Core in-memory provider, or mocked EF behavior as evidence for the end-to-end persistence path.
- Do not add a GET endpoint, production Docker Compose requirement, or production migration runner for test convenience.
- Do not turn the small test chunk size into the accepted operational value or a performance conclusion.
- Do not add retries, deduplication, idempotency, authentication, compression, limits, RabbitMQ, Redis, Outbox, or asynchronous downstream processing.
- Do not start load testing or horizontal-scaling work assigned to later roadmap stages.

### Step 7: Perform the final review and create the completion checkpoint

#### Responsibility introduced

Review the completed slice as one Stage 1 vertical path, reconcile implementation with the accepted contract, ADRs, architecture documentation, and roadmap, and record the resulting repository state.

Run from the repository root:

```powershell
dotnet build PulseFlow.slnx
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Review the code for accidental full-upload buffering, contract/persistence-model reuse, HTTP or EF Core leakage into the handler, premature accepted counts, invalid-record batch rejection, and speculative abstractions or technologies. Confirm that no C# primary constructors were introduced.

Confirm that [Event Contract v1](../contracts/event-ingestion-v1.md) still matches the HTTP behavior accepted in Step 5 and mark its implementation status accurately. Review [ingestion architecture](../architecture/ingestion.md) against the implemented responsibility boundaries. Update the Stage 1 roadmap status only if its complete expected result has been achieved. Create a new immutable progress checkpoint containing the required starting point, changes, resulting state, commands and results, decisions and ADR links, unresolved items, and next recommended step. Mark this plan completed only when every preceding step and this review have passed.

Create a new ADR only if implementation required a significant architectural decision with meaningful alternatives and consequences. Do not use an ADR merely to restate this plan or routine implementation detail.

#### Conceptual files and documentation

- a new immutable checkpoint under `docs/progress/`
- status and implemented-surface updates to `docs/contracts/event-ingestion-v1.md` and `docs/architecture/ingestion.md`
- `docs/02_ROADMAP.md` and this plan's status only when their completion criteria are actually met
- no new application class is expected solely for this review step

#### What this step proves

- The full solution builds and all relevant unit and real-PostgreSQL integration tests pass.
- Documentation distinguishes the implemented pipeline from deferred behavior and agrees with the running application.
- The final checkpoint gives the next session an accurate, reproducible handoff.
- Stage 1 is marked completed only if the clean-environment ingestion result in the roadmap has actually been demonstrated.

#### Explicit non-goals

- Do not expand the milestone to Stage 2 asynchronous processing or Stage 3 reliability work.
- Do not resolve deferred production concerns merely to make the final documentation appear complete.
- Do not rewrite historical checkpoints except to correct factual errors.
- Do not change `01_SOURCE_OF_TRUTH.md` unless implementation actually changes product boundaries or mandatory properties.

## Decisions intentionally deferred

The following remain unresolved until a later implementation step demonstrably requires the decision:

- **Tuned chunk size:** Step 5 supplies a temporary initial operational value of 100 through validated configuration. It is not a performance conclusion and remains subject to measurement.
- **Idempotency and deduplication:** deferred to the reliability stage. This plan does not prevent duplicates after an uncertain client outcome.
- **Retry strategy:** no client, handler, store, EF Core, or PostgreSQL retry policy is selected here.
- **RabbitMQ, Redis, and Outbox:** not part of the Stage 1 ingestion pipeline.
- **Authentication and authorization:** deferred until the corresponding public-surface/security decision.
- **Upload and record-size limits:** required for later hardening, but not selected by this plan. Until then, streaming prevents complete-upload buffering but does not constitute size protection.
- **Compression:** no compressed request format is selected.
- **Production migration execution strategy:** committed migrations remain the schema artifact; how production applies them is separate from application `DbContext` registration and test setup.

Other implementation choices remain just-in-time decision gates at the step where they first become unavoidable. Step 3 resolved identifier and receipt-time assignment, Step 4 resolved application-level persistence-failure propagation, and Step 5 resolved the first public HTTP contract. Unknown top-level Event Contract property behavior remains unresolved. None requires a new assembly or a generic persistence abstraction.

## Completion criteria

PLAN 002 is complete only when:

1. Steps 1 through 6 have been implemented and proved within their stated boundaries.
2. The HTTP ingestion path streams independent NDJSON records and does not buffer the full upload.
3. Valid records are persisted in configurable chunks through `IEventChunkStore`, and only committed records are reported as accepted.
4. Mixed valid, malformed, and invalid input is verified end to end against real PostgreSQL.
5. The endpoint contract and implemented architecture are documented accurately.
6. The three repository verification commands succeed.
7. A new progress checkpoint records the verified result and remaining unresolved decisions.
