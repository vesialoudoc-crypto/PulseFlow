# Checkpoint: Fix ingestion batch-limit review findings

**Date:** 2026-08-25

## Starting point

The committed ingestion body-size limit correctly rejected oversized raw NDJSON
batches, but allocated `MaxBatchBytes` for every request and left Kestrel's default
request-size limit lower than the accepted 100 MiB configuration cap.

## What changed

- Replaced the eager configured-size allocation with a gradually growing
  `ArrayPool<byte>` buffer. It starts at 16 KiB, grows only when the received bytes
  require it, and is always returned after `PublishAsync` or an earlier failure.
- Preserved the byte bound by reading only the logical buffer capacity, never more
  than `MaxBatchBytes`, and retaining the one-byte excess probe.
- Added `DisableRequestSizeLimit` to the ingestion endpoint so Kestrel does not
  preempt the application-owned dynamic limit for valid configurations above its
  default request limit.
- Added a unit test confirming the endpoint metadata disables the server request-size
  limit, and updated architecture, roadmap, and current-state documentation.

## Resulting repository state

Small and slow request bodies no longer allocate the configured maximum payload size
up front. The endpoint remains bounded by `Ingestion:MaxBatchBytes`, returns the same
safe HTTP 413 Problem Details for known and streamed excess data, returns a pooled
buffer only after the asynchronous publisher completes, and owns that behavior up to
the 100 MiB validated cap.

## Verification

- `dotnet test tests/PulseFlow.UnitTests/PulseFlow.UnitTests.csproj --no-restore --filter "FullyQualifiedName~EventsControllerTests|FullyQualifiedName~IngestionOptionsStartupValidationTests"` passed: 9 tests.
- `dotnet test tests/PulseFlow.IntegrationTests/PulseFlow.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~EventAcceptanceTests"` passed: 11 tests.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `pwsh ./scripts/test.ps1` passed: 94 unit tests and 61 integration tests.
- `dotnet csharpier check .` passed: 55 files checked.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `git diff --check` passed with no whitespace errors.

## Decisions made

- A 16 KiB initial pooled buffer is used to avoid tying ordinary request allocation to
  the configured maximum while keeping I/O blocks reasonably sized. It is an
  implementation detail, not a public contract or measured throughput setting.
- `DisableRequestSizeLimit` is endpoint-scoped. The application-level bounded reader
  remains the only request-body-size authority for ingestion, preserving its safe
  HTTP 413 response format.

## Intentionally unresolved

- Record-count, individual-record, compression, decompression, and RabbitMQ
  message-size limits.
- Measured tuning of the default body limit and initial buffer size for deployed
  throughput and memory concurrency.

## Next recommended step

Commit this review-fix slice separately, then continue only with another justified
hardening or deployment prerequisite.
