# Checkpoint: Use root dotenv for AWS staging bootstrap and plan

**Date:** 2026-08-26

## Starting point

The AWS Terraform definition was locally valid, but its documented bootstrap required
a separately maintained AWS CLI profile and a non-committed Terraform variable file.
No authenticated account-specific Terraform plan had been saved. The repository-root
ignored `.env` already contained AWS environment variables, a GHCR package-read
credential, and an immutable PulseFlow image reference, but the AWS tooling did not
use it.

## What changed

- Added the committed safe root [`.env.example`](../../.env.example) with placeholders
  for the required AWS, GHCR, and immutable image variables.
- Added `scripts/Import-PulseFlowDotEnv.ps1`, a shared dotenv loader that supports
  blank lines, full-line comments, `KEY=value`, embedded equals signs, and matching
  optional single or double quotes.
- Changed `scripts/deploy-aws-staging.ps1` so its default action loads root `.env`,
  validates the required variables, uses normal `AWS_*` environment-variable
  discovery, verifies STS identity, creates or identifies the external GHCR secret,
  verifies the immutable private image, saves a real plan, and stops before apply. The
  former rollout path remains available only through explicit `-Deploy` after an
  approved apply.
- Avoided GHCR token command-line exposure by passing the credential JSON to AWS CLI
  through a temporary file that is deleted immediately. Terraform receives only the
  image, region, and GHCR secret ARN; the ARN input is sensitive to avoid plan-output
  disclosure. No real `.tfvars` file is created or required.
- Updated the AWS runbook, Terraform input example, roadmap, and implemented AWS
  architecture record to describe the required `.env` → bootstrap → plan → cost review
  → explicit approval → apply-later flow.

## Resulting repository state

The initial GitHub credential secret has been bootstrapped or identified in AWS Secrets
Manager outside Terraform. The exact credential value was not logged, committed,
passed to Terraform, or stored in Terraform state. The real Terraform plan is saved as
the ignored `infra/aws/pulseflow-staging.tfplan`; it reports **49 to add, 0 to change,
and 0 to destroy**. The resolved RDS PostgreSQL version is 17.11 and the selected
availability zones are `eu-central-1a` and `eu-central-1b`.

No Terraform apply, ECS rollout, migration, service task, smoke test, or
Terraform-managed AWS resource creation has occurred. The only AWS mutation in this
checkpoint is the explicitly required external GHCR credential-secret bootstrap.

## Verification

- PowerShell parser validation passed for `scripts/deploy-aws-staging.ps1` and
  `scripts/Import-PulseFlowDotEnv.ps1`.
- The shared loader successfully loaded all required variables from root `.env`
  without printing their values.
- `pwsh ./scripts/deploy-aws-staging.ps1` correctly stopped before AWS access when its
  immutable-image validation was malformed; the validation was corrected before the
  successful run.
- With the existing local Terraform and AWS CLI directories temporarily added to the
  validation process PATH, `pwsh ./scripts/deploy-aws-staging.ps1` completed STS,
  GHCR secret bootstrap/identification, GHCR immutable-image verification,
  `terraform fmt -check -recursive`, `terraform init`, `terraform validate`, and
  `terraform plan -out pulseflow-staging.tfplan` successfully.
- The saved plan reports 49 creates, 0 changes, and 0 destroys. `terraform apply` was
  not invoked.

## Decisions made

- Root `.env` is the documented local bootstrap source for the AWS staging workflow;
  an AWS CLI/shared-credentials profile is not required. Existing `aws_profile`
  Terraform support remains an explicit optional override.
- The existing external-GHCR-secret boundary from
  [ADR 0021](../decisions/0021-define-first-aws-staging-resource-configuration.md)
  remains unchanged. This checkpoint implements its reproducible local bootstrap path
  without placing the GHCR token in Terraform state.
- The existing AWS selection and staging boundary from
  [ADR 0020](../decisions/0020-use-aws-for-first-cloud-deployment.md) remain
  unchanged; no new architectural decision or ADR is required.

## Intentionally unresolved

- Full review of all 49 proposed resources, the updated cost estimate, and one explicit
  owner approval for paid resources.
- Terraform apply, migration result, API health responses, end-to-end ingestion proof,
  teardown evidence, and observed operating cost.
- GHCR credential rotation procedure, remote state, production networking/egress,
  HTTPS/domain, RDS certificate verification, restricted RabbitMQ user management,
  automatic deployment, and Azure portability proof.

## Next recommended step

Review `infra/aws/pulseflow-staging.tfplan` and the AWS staging cost checkpoint.
Obtain explicit approval for the 49 planned creates before running
`terraform apply pulseflow-staging.tfplan`; only after that apply succeeds, run
`pwsh ./scripts/deploy-aws-staging.ps1 -Deploy` from the repository root.
