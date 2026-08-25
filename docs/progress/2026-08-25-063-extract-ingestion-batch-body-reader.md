# Checkpoint: Extract ingestion batch body reader

**Date:** 2026-08-25

## Starting point

The ingestion endpoint already bounded raw NDJSON request bodies with a gradually
growing pooled buffer, but `EventsController` also implemented the low-level read,
growth, probe, and pool-return lifecycle itself.

## What changed

- Added `IIngestionBatchBodyReader`, the internal `PooledIngestionBatchBodyReader`,
  and `IngestionBatchBodyReadResult` at the ingestion HTTP boundary.
- Moved Content-Length rejection, progressive pooled reads, logical-capacity growth,
  one-byte probing, and all reader-side buffer cleanup into the reader.
- Made successful read results own their final buffer and expose only their exact
  filled `ReadOnlyMemory<byte>` range. Disposing is idempotent and returns the buffer
  once.
- Reduced `EventsController` to rate-limit orchestration, reader-result mapping to
  HTTP 413 Problem Details, publishing, and HTTP 202 response handling.
- Registered the stateless reader as a singleton. It stores only the startup-validated
  size limit and shared pool; request-specific ownership remains in each result.
- Added direct reader tests for limits, untrusted lengths, expansion, cancellation,
  read failure, and pooled-resource lifetime. Controller tests now focus on
  orchestration and result ownership across publisher success, failure, and delay.

## Resulting repository state

The public endpoint retains its existing 202, 413 Problem Details, cancellation,
OpenAPI, `DisableRequestSizeLimit`, 10 MiB default, and 100 MiB hard-cap behavior.
No oversized request is published. The reader returns current buffers on too-large,
read-exception, and cancellation paths; a successful result keeps its buffer until
the publisher task has finished, then the controller disposes it.

## Verification

- `dotnet test tests/PulseFlow.UnitTests/PulseFlow.UnitTests.csproj --no-restore --filter "FullyQualifiedName~EventsControllerTests|FullyQualifiedName~IngestionBatchBodyReader"` passed: 19 tests.
- `dotnet test tests/PulseFlow.IntegrationTests/PulseFlow.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~EventAcceptanceTests"` passed: 11 tests.
- `pwsh ./scripts/test.ps1` passed: 108 unit tests and 61 integration tests.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `dotnet csharpier check .` passed: 58 files checked.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `git diff --check` passed with no whitespace errors.

## Decisions made

- The bounded reader is singleton because it has no mutable request state. Its only
  fields are immutable validated configuration and `ArrayPool<byte>.Shared`; the
  disposable result owns every request buffer.
- The reader returns a technical `Success` or `TooLarge` result without MVC types.
  The controller remains the owner of HTTP/OpenAPI concerns and Problem Details.

## Intentionally unresolved

- Record-count, individual-record, compression, decompression, and RabbitMQ
  message-size limits.
- Measured tuning of the initial 16 KiB capacity for deployed traffic.

## Next recommended step

Run the complete repository verification suite and commit this focused architectural
refactor separately if all checks pass.
