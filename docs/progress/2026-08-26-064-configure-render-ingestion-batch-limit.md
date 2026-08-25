# Checkpoint: Configure Render ingestion batch limit

**Date:** 2026-08-26

## Starting point

The application startup-validates `Ingestion:MaxBatchBytes` with a 10 MiB default and
100 MiB cap, but the Render web-service Terraform configuration did not explicitly
provide that runtime setting.

## What changed

- Added the `ingestion_max_batch_bytes` Terraform input with a 10 MiB default.
- Validated that it is an integer number of bytes from 1 through 100 MiB, matching
  `IngestionOptions.DefaultMaxBatchBytes` and `IngestionOptions.MaximumMaxBatchBytes`.
- Passed it to `render_web_service.api` as `Ingestion__MaxBatchBytes` beside
  `Ingestion__ChunkCapacity`.
- Documented the runtime configuration and clarified that it limits raw HTTP body
  bytes rather than NDJSON record count or RabbitMQ message size.

## Resulting repository state

Render staging can manage the application's existing ingestion body-size setting
through Terraform without changing the application default or hard cap. No Render
resources, provider configuration, connection strings, or deployment behavior changed.

## Verification

- `terraform fmt -check`, `terraform init -backend=false`, and `terraform validate`
  could not run because Terraform is not installed or available on the local PATH.
  It was not installed for this task.
- `dotnet csharpier check .` passed: 58 files checked.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `pwsh ./scripts/test.ps1` passed: 108 unit tests and 61 integration tests.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `git diff --check` passed with no whitespace errors.
- Terraform plan and apply are intentionally out of scope and were not run.

## Decisions made

- The Terraform bounds deliberately mirror the existing application validation. The
  variable description identifies the two application constants so repeated numeric
  values remain traceable rather than becoming unexplained infrastructure limits.

## Intentionally unresolved

- Terraform plan/apply and creating Render staging resources remain deferred pending
  controlled credentials, account, cost, and state-storage review.

## Next recommended step

Run static Terraform verification, review the narrow diff, and commit the
configuration fix separately if all checks pass.
