# Checkpoint: Asynchronous NDJSON record reader implemented

**Date:** 2026-08-17

## Starting point

[PLAN 002 Step 1](2026-08-16-012-event-envelope-validation-boundary.md) had
implemented the untrusted parsed JSON, Event Contract v1 validation, and valid
`EventEnvelope` boundary. NDJSON framing edge cases, stream reading, JSON syntax
outcomes, record numbering, and cancellation behavior remained unresolved and
unimplemented.

ADR 0001 required independent NDJSON records without heuristic repair, but it did not
define LF/CRLF details, blank-line behavior, or clean final JSON without a trailing
newline. The repository also had no permanent project-level test convention.

This milestone implements only Step 2 of
[PLAN 002](../plans/002-stage-1-ingestion-pipeline.md).

## What changed

- Added a concise `Testing` section to `AGENTS.md` with these permanent rules:
  - every test uses explicit `// Arrange`, `// Act`, `// Assert`;
  - names follow `Method_Scenario_ExpectedResult`;
  - one test verifies one logical behavior or scenario;
  - avoid Eager Test and unrelated behaviors in one test;
  - avoid Assertion Roulette; assertions in one test belong to one logical result;
  - multiple assertions may collectively verify one logical result;
  - do not split one logical result into several nearly identical one-assertion tests;
  - prefer `[Theory]` for multiple inputs exercising the same behavior;
  - avoid duplicated Arrange/Act code across nearly identical tests;
  - prefer readable, sufficient tests over exhaustive permutation or coverage-driven
    suites;
  - helpers improve readability without hiding intent;
  - unrelated existing tests are not refactored unless explicitly requested.
- Added [ADR 0004](../decisions/0004-define-ndjson-record-framing.md), accepting LF
  and CRLF terminators, malformed blank records, tolerant clean final JSON without a
  newline, malformed incomplete final data without repair, one-based physical record
  numbering, cancellation propagation, and a parsed-or-malformed syntax outcome only.
- Added `NdjsonRecordReader`, which incrementally reads a supplied `Stream` and returns
  `IAsyncEnumerable<NdjsonRecordResult>` with cancellation support.
- Added the deliberately small `NdjsonRecordResult`; no generic result framework,
  parser abstraction, factory/strategy hierarchy, or parser error-code taxonomy was
  introduced.
- Added focused `NdjsonRecordReaderTests` covering the framing and parser behavior
  required by ADR 0004, retained JSON ownership, continued reading after malformed
  input, ordering, numbering, and cancellation.
- Updated PLAN 002 and the accepted ingestion architecture to describe Step 2 as
  implemented and link the exact framing rules. PLAN 002 and Stage 1 remain in
  progress.

## Resulting repository state

The implemented Step 2 boundary is:

```text
Stream
    ↓
LF / CRLF framing
    ↓
one JSON syntax parse per record
    ↓
NdjsonRecordResult
    ├─ parsed: one-based record number + independently owned JsonElement
    └─ malformed: one-based record number + malformed marker
```

`NdjsonRecordReader` exposes:

```csharp
IAsyncEnumerable<NdjsonRecordResult> ReadAsync(
    Stream stream,
    CancellationToken cancellationToken = default)
```

`NdjsonRecordResult` has exactly these public members:

- `long RecordNumber` — one-based physical stream order;
- `bool IsMalformed` — true only for a JSON syntax failure;
- `JsonElement? ParsedJson` — present only for syntactically valid JSON.

The reader uses a fixed 4096-byte I/O buffer and one `ArrayBufferWriter<byte>` for the
current record. It scans each read buffer for LF, appends only the current record's
segments, removes the preceding CR when the boundary is CRLF, parses the completed
record, yields its outcome, and starts a new current-record buffer. It never
materializes the full upload.

Each record is passed to `JsonDocument.Parse` exactly once. A successful root element
is cloned into `NdjsonRecordResult` before the temporary `JsonDocument` is disposed.
Only `JsonException` becomes a malformed result, so cancellation and stream failures
propagate normally. After a malformed newline-terminated record, iteration continues
at the next explicit record boundary while the stream remains readable.

The reader performs no Event Contract validation and does not construct
`EventEnvelope`. The existing `EventEnvelopeValidator` remains the next separate
boundary for parsed untrusted JSON. There is still no handler, chunking, chunk store,
application database wiring, DI registration, or HTTP endpoint.

## Verification

Required commands run from the repository root:

```powershell
dotnet build PulseFlow.slnx
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- `dotnet build PulseFlow.slnx` succeeds with 0 warnings and 0 errors.
- `dotnet test PulseFlow.slnx` succeeds with 59 unit tests and 28 integration tests
  passed, 0 failed, and 0 skipped. The integration suite used the existing real
  PostgreSQL Testcontainers fixture and local Docker engine.
- `pwsh ./scripts/check-project-docs.ps1` completes without warnings.
- The sandbox-restricted first restore attempts could not reach NuGet; the exact build
  and test commands succeeded after approved network access was granted. No package or
  project dependency changed.

## Decisions made

- Accepted [ADR 0004](../decisions/0004-define-ndjson-record-framing.md). A complete
  final JSON value without a newline is tolerated because JSON syntax still provides
  an exact validity boundary; incomplete or malformed final data is never repaired.
- Used one small per-record result with only a record number, malformed indicator, and
  optional parsed JSON. Detailed JSON parser categories are unnecessary for Step 2.
- No additional architectural decision became unavoidable. The 4096-byte read buffer
  and concrete type placement are internal implementation details, not public
  ingestion contracts.

## Intentionally unresolved

- Unknown top-level Event Contract property behavior.
- Record, upload, record-count, field-length, payload-depth, and other size limits.
- HTTP method, route, media type behavior, status codes, and response/error shapes.
- Identifier generation, receipt-time assignment, mapping to `EventRecord`,
  `IEventChunkStore`, chunk formation, orchestration, and application database wiring.
- Concrete chunk size, persistence-failure propagation, retry, idempotency,
  deduplication, compression, authentication, messaging, and downstream processing.

## Next recommended step

Implement only PLAN 002 Step 3: add the narrow `IEventChunkStore` durability boundary
and its EF Core implementation. Before mapping valid envelopes to persistence records,
make the smallest explicit choice for identifier generation and receipt-time
assignment. Do not create the ingestion handler, choose chunk boundaries, add HTTP or
DI wiring, or introduce retry and reliability behavior in that step.
