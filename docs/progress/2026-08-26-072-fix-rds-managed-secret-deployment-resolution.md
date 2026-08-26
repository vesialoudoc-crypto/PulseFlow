# Checkpoint: Fix RDS-managed master-secret deployment resolution

**Date:** 2026-08-26

## Starting point

AWS staging infrastructure had already been applied and remained running. The first
real `pwsh ./scripts/deploy-aws-staging.ps1 -Deploy` attempt stopped with
`The property 'host' cannot be found on this object.` The deployment script treated
the RDS-managed master credential secret as if it also contained the database endpoint,
port, and database name.

The failure occurred before the migration task, ECS desired-count update, and smoke
test. No deployment-side resource changes were completed by the failed attempt.

## What changed

- Added `scripts/Resolve-PulseFlowRdsConnectionMetadata.ps1`, which parses the JSON
  returned by `rds describe-db-instances` and selects the only DB instance whose
  `MasterUserSecret.SecretArn` exactly matches the existing Terraform output
  `rds_master_user_secret_arn`.
- Changed `scripts/deploy-aws-staging.ps1` to continue reading only `username` and
  `password` from the RDS-managed secret, with clear presence validation.
- Changed the deployment script to retrieve RDS metadata through its existing
  `Invoke-Aws` wrapper using `rds describe-db-instances --output json`, then validate
  the single matching instance's `Endpoint.Address`, `Endpoint.Port`, and `DBName`.
- Built the PostgreSQL connection string from those two distinct sources while
  preserving `Ssl Mode=Require;Trust Server Certificate=true`.
- Added mocked Pester coverage for successful metadata resolution and for missing and
  ambiguous DB-instance matches. The test data contains no real AWS identifiers or
  credentials.

## Resulting repository state

The RDS-managed secret remains credential-only. No Terraform outputs, RDS resources,
Secrets Manager ownership, migration sequencing, ECS rollout sequencing, or smoke-test
behavior changed. The deployment script does not log the RDS password or the resulting
PostgreSQL connection string.

The existing staging infrastructure was neither recreated nor destroyed. This change
does not alter the current ECS desired count; resuming `-Deploy` will again start from
the migration-first deployment step.

## Verification

- PowerShell parser validation passed for the deployment script, the RDS metadata
  helper, and its Pester test file.
- `Invoke-Pester -Script tests/scripts/Resolve-PulseFlowRdsConnectionMetadata.Tests.ps1 -PassThru`
  passed 3 tests: valid mocked RDS metadata resolution, no matching instance, and two
  matching instances.
- No Terraform plan, apply, or destroy; ECS deployment; migration; smoke test; or AWS
  mutation was run while making or validating this fix.

## Decisions made

- RDS connection metadata is resolved from the live DB-instance description rather
  than inferred from a credential secret or added as a Terraform output. This is a
  deployment-script defect correction inside the accepted AWS staging architecture;
  no ADR is required.

## Intentionally unresolved

- The resumed deployment's migration result, ECS rollout, health checks, end-to-end
  ingestion proof, and teardown evidence have not yet been observed.
- GHCR credential rotation, remote state, production networking/egress, HTTPS/domain,
  RDS certificate verification, restricted RabbitMQ user management, automatic
  deployment, and Azure portability proof remain unresolved.

## Next recommended step

Resume the already applied staging deployment from the repository root:

```powershell
pwsh ./scripts/deploy-aws-staging.ps1 -Deploy
```

If it completes, record the migration, rollout, health, and smoke-test evidence in a
new checkpoint before any teardown decision.
