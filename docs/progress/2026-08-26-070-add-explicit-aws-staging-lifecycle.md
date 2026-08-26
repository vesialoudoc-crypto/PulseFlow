# Checkpoint: Add explicit AWS staging lifecycle commands

**Date:** 2026-08-26

## Starting point

The default AWS staging command loaded root `.env`, bootstrapped or identified the
external GHCR credential secret, verified the selected immutable image, and created
the reviewed `infra/aws/pulseflow-staging.tfplan` without applying it. The rollout and
smoke proof already required `-Deploy`, but applying the saved plan was a separate
manual Terraform command and there was no scripted teardown path. Checkpoint 069
records the existing saved plan with 49 creates and no Terraform-managed AWS resources
applied.

## What changed

- Added mutually exclusive `-Apply`, `-Deploy`, and `-Destroy` parameter sets to
  `scripts/deploy-aws-staging.ps1`; the no-switch default remains the plan-only mode.
- Added `-Apply`, which loads root `.env` through the shared loader, validates AWS
  identity, requires the existing saved plan, initializes Terraform, and applies only
  that plan. It never creates a replacement plan or starts `-Deploy`. A successful
  apply explicitly reports that the ECS service remains at desired count zero.
- Added `-Destroy`, which loads the same root `.env`, validates AWS identity, resolves
  the existing external GHCR bootstrap-secret ARN, and runs Terraform destroy with the
  same non-secret inputs: AWS region, immutable image, and GHCR secret ARN. The GHCR
  token is not passed to Terraform. The command requires a successful destroy and an
  empty `terraform state list` afterward.
- Added `-DeleteBootstrapSecret`, valid only with `-Destroy`. It deletes the external
  `pulseflow-staging/bootstrap/ghcr` secret only after Terraform destruction and empty
  state verification succeed. Normal `-Destroy` explicitly leaves this non-
  Terraform-managed reusable bootstrap secret in place.
- Updated the AWS runbook, current roadmap entry, and accepted AWS-staging architecture
  record with the explicit `PLAN → APPLY → DEPLOY + VERIFY → DESTROY` workflow.

## Resulting repository state

The staging command interface is now:

```powershell
pwsh ./scripts/deploy-aws-staging.ps1
pwsh ./scripts/deploy-aws-staging.ps1 -Apply
pwsh ./scripts/deploy-aws-staging.ps1 -Deploy
pwsh ./scripts/deploy-aws-staging.ps1 -Destroy
pwsh ./scripts/deploy-aws-staging.ps1 -Destroy -DeleteBootstrapSecret
```

The default command remains the only plan-generating mode. `-Apply` consumes the
existing reviewed plan, and deployment remains a distinct explicit command. Normal
teardown removes only Terraform-managed staging resources; the external GHCR bootstrap
credential remains reusable unless the owner explicitly supplies
`-DeleteBootstrapSecret`.

No Terraform apply, Terraform destroy, ECS deployment, migration, smoke test, AWS
resource creation, or AWS resource deletion was run while making this change. The
previously existing external bootstrap secret and saved plan were not modified.

## Verification

- PowerShell parser validation passed for `scripts/deploy-aws-staging.ps1` and
  `scripts/Import-PulseFlowDotEnv.ps1`.
- `Get-Command ./scripts/deploy-aws-staging.ps1` reports exactly the `Plan`, `Apply`,
  `Deploy`, and `Destroy` parameter sets.
- Non-mutating PowerShell invocations correctly rejected `-Apply -Deploy`,
  `-Apply -Destroy`, and `-Deploy -Destroy` during parameter binding.
  `-DeleteBootstrapSecret` without `-Destroy` was rejected before `.env` loading or
  AWS access.
- A dry `-Apply` check using a mocked STS identity and a nonexistent Terraform
  directory failed with the explicit missing-saved-plan message before Terraform apply
  could be invoked.
- `terraform fmt -check -recursive`, `terraform init -backend=false -input=false`,
  and `terraform validate` passed in `infra/aws/` with Terraform 1.15.9 and the locked
  AWS 6.61.0 and Random 3.9.0 providers. Backend-free initialization did not contact
  AWS.
- `git diff --check` passed before the repository-wide final checks.

## Decisions made

- The explicit lifecycle is an operational-tooling improvement within the existing
  accepted disposable AWS staging boundary. It does not change the AWS architecture,
  resource topology, resource sizes, state boundary, or external GHCR-secret boundary
  accepted in ADR 0021; no new ADR is required.
- The external GHCR credential secret remains intentionally outside Terraform. Its
  deletion requires the separate, owner-visible `-DeleteBootstrapSecret` switch after
  successful Terraform teardown.

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
`pwsh ./scripts/deploy-aws-staging.ps1 -Apply`; then run `-Deploy` separately for the
migration and smoke proof. Record the evidence, then use `-Destroy` to remove the
disposable Terraform-managed environment. Add `-DeleteBootstrapSecret` only if the
external GHCR bootstrap credential should also be permanently removed.
