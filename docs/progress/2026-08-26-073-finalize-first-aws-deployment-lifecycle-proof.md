# Checkpoint: Finalize first AWS deployment lifecycle proof

**Date:** 2026-08-26

## Starting point

Checkpoint 072 corrected deployment-time RDS metadata resolution while the first
applied AWS staging environment still existed. The resumed real deployment then
completed successfully, and normal `-Destroy` removed the Terraform-managed
environment. Terraform reported 49 destroyed, but the script's post-destroy check
failed because an empty filtered PowerShell pipeline became `$null` under
`Set-StrictMode`, making `.Count` invalid. A manual non-mutating `terraform state
list` returned empty. The external GHCR bootstrap secret remained intentionally
reusable because `-DeleteBootstrapSecret` was not used.

## What changed

- Added `scripts/Get-PulseFlowRemainingTerraformStateResources.ps1`, a small pure
  helper that explicitly normalizes nonblank Terraform state-list lines to an array.
- Changed `Invoke-AwsStagingDestroy` to capture `terraform state list` output as an
  array, preserve its existing nonzero-exit failure, and wrap the helper result in an
  explicit array before checking `.Count`.
- Added focused Pester coverage for an empty state list, one remaining resource, and
  multiple remaining resources.
- Updated the Stage 5 roadmap and AWS staging architecture record with the observed
  first lifecycle and the next deployed-measurement objective.

## Resulting repository state

The destroy verification now has these outcomes while retaining `Set-StrictMode`:

- zero nonblank `terraform state list` lines succeeds;
- one or more nonblank lines causes the existing clear failure; and
- a failed `terraform state list` command causes the existing clear failure before
  remaining-resource inspection.

No Terraform resources, AWS networking, RDS, RabbitMQ, Valkey, ECS sequencing,
smoke-test behavior, GHCR-secret ownership, or root `.env` convention changed.

The first real AWS lifecycle is now recorded as observed evidence:

- Terraform apply: 49 added, 0 changed, 0 destroyed.
- Deployment: the migration ECS task succeeded; public-ALB `/health/live` and
  `/health/ready` returned HTTP 200; the ingestion smoke POST returned HTTP 202; the
  exact event was verified in PostgreSQL through the in-VPC verifier task; and the
  exact smoke row was removed.
- Terraform destroy: 49 destroyed. The post-destroy `.Count` bug then occurred, but
  a manual non-mutating `terraform state list` returned empty, confirming teardown
  of the Terraform-managed staging environment.
- The external GHCR bootstrap secret intentionally remains reusable after normal
  `-Destroy`.

The initial deployment also exposed and corrected a separate tooling defect: the
RDS-managed master secret contains credentials rather than endpoint metadata. The
deployment script now obtains the endpoint, port, and database name from
`rds describe-db-instances` and reads only the username and password from the
credential secret.

## Verification

- PowerShell parser validation passed for all 9 scripts and script-test files.
- `Invoke-Pester -Script tests/scripts -PassThru` passed 6 tests, including the 3 new
  Terraform-state helper cases and the existing 3 RDS-metadata helper cases.
- `terraform fmt -check -recursive` passed in `infra/aws/`.
- `terraform init -backend=false -input=false` and `terraform validate` passed with
  Terraform 1.15.8 and the locked AWS 6.61.0 and Random 3.9.0 providers. No backend,
  plan, apply, destroy, or AWS CLI command was run.
- `dotnet csharpier check .` passed for 63 files.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `dotnet test tests/PulseFlow.UnitTests/PulseFlow.UnitTests.csproj --no-build`
  passed 135 tests.
- `pwsh ./scripts/test.ps1` was attempted but could not run its Testcontainers suite
  because no Docker daemon was available. It performed no AWS action.
- `pwsh ./scripts/check-project-docs.ps1` passed after staging the checkpoint and
  documentation updates.
- `git diff --cached --check` passed.

## Decisions made

- The explicit-array state normalization is a narrow deployment-tooling correction;
  it does not change the accepted AWS architecture or lifecycle. No ADR is required.
- Stage 5 remains in progress. The first disposable deployment lifecycle is proven,
  but automatic deployment/CI/CD and deployed observability/performance work are not
  complete.

## Intentionally unresolved

- Deployed observability and performance measurement: a short controlled AWS load
  run, one measured bottleneck, and one justified before/after optimization.
- Automatic CI/CD deployment, remote Terraform state, production networking/egress,
  HTTPS/domain, RDS certificate verification, restricted RabbitMQ user management,
  production HA, and the Azure portability proof.
- Full Testcontainers integration-suite verification requires a running Docker daemon.

## Next recommended step

Prepare the deployed observability/performance slice before recreating the disposable
environment: define a short controlled AWS load run and the signals needed to identify
one measured bottleneck. Do not resume local bottleneck tuning. Any future AWS
lifecycle still requires the documented cost review and explicit approval before
`-Apply`, followed by normal teardown when measurement is complete.
