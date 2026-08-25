# Checkpoint: Limit ingestion batch request-body size

**Date:** 2026-08-25

## Starting point

`POST /api/events` copied every raw NDJSON request body to an unbounded
`MemoryStream`, then called `ToArray`, creating a second complete payload copy. The
application had no batch-body limit; an oversized request could reach RabbitMQ as long
as an upstream server or platform allowed it.

## What changed

- Added startup-validated `Ingestion:MaxBatchBytes` as a `long` option. The default
  configuration and local Compose topology use 10 MiB (`10485760` bytes), and the
  named hard configuration cap is 100 MiB.
- Replaced the unbounded `MemoryStream`/`ToArray` path with one configured-size byte
  buffer. The publisher receives only its filled `ReadOnlyMemory<byte>` range.
- Rejected an oversized known `Content-Length` with HTTP 413 before request-body
  reading or payload-buffer allocation.
- Bound unknown-length and chunked bodies while reading: after the allowed byte count,
  the controller reads one probe byte and returns the same HTTP 413 response when it
  exists. It does not drain the remaining body or call the publisher.
- Added safe `application/problem+json` HTTP 413 Problem Details with status, safe
  title/detail, and the current request trace ID; documented it in OpenAPI.
- Preserved request-abort cancellation by allowing it to propagate and preventing the
  global exception handler from mapping request-abort `OperationCanceledException` to
  HTTP 500.
- Added focused unit and HTTP tests for the byte boundaries, early no-read rejection,
  unknown/chunked streaming rejection, exact published bytes, Problem Details,
  startup validation, and cancellation.
- Updated the accepted Event Contract, ingestion and staging architecture documents,
  local Compose settings, and the Stage 6 roadmap state.

## Resulting repository state

The HTTP API now owns an application-level raw-body bound independent of Kestrel,
proxies, and deployment platforms. A body smaller than or exactly equal to the
configured value is published unchanged and returns HTTP 202. Any body with more bytes
returns HTTP 413 and never reaches RabbitMQ. The HTTP API continues not to parse NDJSON,
validate records, or count records.

## Verification

- `dotnet test tests/PulseFlow.UnitTests/PulseFlow.UnitTests.csproj --no-restore --filter "FullyQualifiedName~EventsControllerTests|FullyQualifiedName~IngestionOptionsStartupValidationTests"` passed: 8 tests.
- `dotnet test tests/PulseFlow.IntegrationTests/PulseFlow.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~EventAcceptanceTests"` passed: 11 tests.
- `pwsh ./scripts/test.ps1` passed: 93 unit tests and 61 integration tests.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `dotnet csharpier check .` passed: 55 files checked.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `git diff --check` passed with no whitespace errors.

## Decisions made

- The configuration default is 10 MiB and its hard cap is 100 MiB because the existing
  repository had no accepted batch-body size or fixture-size baseline. This is a
  conservative request-bound choice, not a measured throughput conclusion.
- No ADR was added: this is a narrow implementation of the explicitly required
  application-level input bound and does not alter RabbitMQ topology, delivery
  semantics, or the asynchronous ingestion boundary.

## Intentionally unresolved

- Record-count, individual-record, compression, decompression, and RabbitMQ
  message-size limits.
- Measured tuning of the default body limit for deployed throughput and memory
  concurrency.
- Request authentication, authorization, and other public-surface hardening work.

## Next recommended step

Continue the next justified Stage 6 hardening or deployment prerequisite as a separate
small verified slice; do not infer record, compression, or broker-message limits from
this HTTP body-size boundary.
