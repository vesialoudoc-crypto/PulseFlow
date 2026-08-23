# Checkpoint: Add Docker test preflight

**Date:** 2026-08-23

## Starting point

The full local test suite contains Testcontainers integration tests. When Docker was
not running, `dotnet test PulseFlow.slnx` started the suite and then produced noisy
fixture-startup failures that did not clearly identify the local Docker daemon as the
first prerequisite.

## What changed

- Added `scripts/test.ps1` as the local entry point for the full test suite.
- The script runs `docker info` first. If Docker Desktop or Docker Engine is
  unavailable, it reports that Testcontainers integration tests cannot run, exits
  with code 1, and does not start `dotnet test`.
- When Docker is available, the script runs `dotnet test PulseFlow.slnx` and returns
  its exit code.
- Updated the standard local verification flow in `AGENTS.md` to use
  `pwsh ./scripts/test.ps1` in place of the direct test command.
- GitHub Actions behavior remains unchanged.

## Resulting repository state

Developers can run the full local suite through `pwsh ./scripts/test.ps1`. The script
does not skip or alter Testcontainers integration tests; it only reports the missing
Docker prerequisite before the test suite starts.

## Verification

Commands run from the repository root:

```powershell
pwsh ./scripts/test.ps1
dotnet csharpier check .
dotnet build PulseFlow.slnx -warnaserror
pwsh ./scripts/check-project-docs.ps1
```

Results:

- Docker was available, so `scripts/test.ps1` ran the full suite successfully:
  80 unit tests and 51 integration tests passed; no tests were skipped or failed.
- `dotnet csharpier check .` completed successfully.
- `dotnet build PulseFlow.slnx -warnaserror` completed successfully with 0 warnings
  and 0 errors.
- `pwsh ./scripts/check-project-docs.ps1` completed successfully.

## Decisions made

No architecture or product decision changed. This is a local developer-experience
improvement for the existing Testcontainers test prerequisite.

## Intentionally unresolved

- Stage 4 multi-instance rate-limit and load verification remains outstanding.
- The script checks only Docker availability; it does not diagnose individual
  container-image, network, or test-environment failures.

## Next recommended step

Continue the Stage 4 multi-instance rate-limit verification or define the first
reproducible load scenario.
