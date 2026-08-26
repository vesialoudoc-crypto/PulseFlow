# Checkpoint: Resolve local AWS tooling executables without PATH edits

**Date:** 2026-08-26

## Starting point

The explicit AWS staging lifecycle was available through
`scripts/deploy-aws-staging.ps1`, including plan-only default behavior, saved-plan-only
apply, separate deployment, and Terraform teardown. The script required both
`terraform` and `aws` to be present on the current process `PATH`, even though the
tools were installed on Windows. A new terminal without those installation directories
in `PATH` stopped before any lifecycle command with a missing-tool error.

## What changed

- Added `scripts/Resolve-PulseFlowExecutable.ps1`, a reusable resolver that checks
  `PATH` with `Get-Command` first, tests caller-provided full candidate paths, returns
  a canonical full executable path, optionally prefers an exact version, and reports
  every checked location plus a `winget` installation command when no executable is
  found.
- Added Terraform discovery for the current user's WinGet package directory. It checks
  the standard HashiCorp package path and enumerates matching
  `Hashicorp.Terraform_*` package directories.
- Added AWS CLI discovery for standard Windows AWS CLI v2 locations, including
  `C:\Program Files\Amazon\AWSCLIV2\aws.exe`, the 64-bit Program Files location, and
  the x86 Program Files location.
- Changed every Terraform and AWS CLI call in the staging script to invoke the resolved
  full executable path. This covers Terraform output, formatting, initialization,
  validation, plan, apply, destroy, and state; and AWS STS, Secrets Manager, ECS, and
  Amazon MQ operations.
- Added a current saved-plan guard: `-Apply` requires the resolved Terraform version
  to be exactly 1.15.8 before the script can invoke Terraform apply. It never
  regenerates the reviewed plan when the version differs.
- Updated the AWS runbook and Stage 5 roadmap record to state that manual `PATH`
  editing is not part of the normal workflow. The tools still must be installed; the
  script does not install them.

## Resulting repository state

On this Windows machine, the resolver selected:

- Terraform 1.15.8 at
  `C:\Users\TDA\AppData\Local\Microsoft\WinGet\Packages\Hashicorp.Terraform_Microsoft.Winget.Source_8wekyb3d8bbwe\terraform.exe`.
- AWS CLI v2.36.29 at `C:\Program Files\Amazon\AWSCLIV2\aws.exe`.

Neither executable needed to be added to the process or global `PATH`. The repository
root `.env` parsing and required-variable behavior remain unchanged.

No Terraform apply, Terraform destroy, ECS deployment, migration, smoke test, AWS
resource creation, or AWS resource deletion was run while making this change.

## Verification

- PowerShell parser validation passed for `scripts/Resolve-PulseFlowExecutable.ps1`,
  `scripts/deploy-aws-staging.ps1`, and `scripts/Import-PulseFlowDotEnv.ps1`.
- Direct resolver validation selected the full Terraform path above with version 1.15.8
  and the full AWS CLI v2 path above with reported version `aws-cli/2.36.29`.
- Non-mutating resolver checks verified PATH discovery, fallback discovery,
  preferred-version selection, and missing-tool error messages that include the checked
  location and `winget` installation command.
- Non-mutating PowerShell invocations continue to reject `-Apply -Deploy`,
  `-Apply -Destroy`, `-Deploy -Destroy`, and `-DeleteBootstrapSecret` without
  `-Destroy`.
- `terraform fmt -check -recursive`, `terraform init -backend=false -input=false`,
  and `terraform validate` passed in `infra/aws/` with the resolved Terraform 1.15.8.
  Backend-free initialization did not contact AWS.
- `dotnet csharpier check .` and `dotnet build PulseFlow.slnx -warnaserror` passed.
  The standalone unit suite passed 135 tests.
- `pwsh ./scripts/check-project-docs.ps1` and `git diff --check` passed.
- `pwsh ./scripts/test.ps1` could not start Testcontainers integration tests because
  Docker Desktop / Docker Engine is not running. This is an environment limitation;
  start Docker and rerun the command to complete the full suite.

## Decisions made

- Executable discovery is local development tooling only. It changes neither the AWS
  architecture nor its resource configuration, credentials, state model, or external
  GHCR-secret boundary; no ADR is required.
- The saved plan's required Terraform version is explicitly 1.15.8 for the current
  reviewed plan. A newly reviewed plan may require a separately recorded version update
  rather than an implicit plan regeneration during apply.

## Intentionally unresolved

- Full review of the saved plan's 49 proposed resources, the cost checkpoint, and one
  explicit approval for paid resources.
- A real apply, migration/rollout result, health responses, end-to-end ingestion proof,
  teardown evidence, and observed operating cost.
- GHCR credential rotation, remote state, production networking/egress, HTTPS/domain,
  RDS certificate verification, restricted RabbitMQ user management, automatic
  deployment, and Azure portability proof.

## Next recommended step

Review `infra/aws/pulseflow-staging.tfplan` and the AWS staging cost checkpoint. After
one explicit owner approval for the paid resources, run
`pwsh ./scripts/deploy-aws-staging.ps1 -Apply`; its Terraform 1.15.8 guard will require
the reviewed-plan-compatible executable before apply. Then run `-Deploy` separately for
the migration and smoke proof, record the evidence, and use `-Destroy` to remove the
disposable Terraform-managed environment.
