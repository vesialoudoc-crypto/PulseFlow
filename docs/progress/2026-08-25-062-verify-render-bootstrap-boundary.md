# Checkpoint: Verify Render bootstrap boundary

**Date:** 2026-08-25

## Starting point

The first Render staging Terraform definition and immutable-image migration bundle
were present at commit `9bd497769c15f4d381712dfbe9fc0f9883a1341c`, but no Render
account bootstrap inputs, Terraform state, or Render resources existed.

## What changed

- Audited the committed configuration against the official
  `render-oss/render` Terraform provider 1.9.1 documentation.
- Confirmed that provider authentication can remain external through
  `RENDER_API_KEY` and `RENDER_OWNER_ID`, and that the configuration uses only
  supported provider 1.9.1 arguments and read-only attributes.
- Confirmed current Render cost and free-tier boundaries before the first plan.
- Added `*.tfplan` to `.gitignore`; the documented local saved-plan filename,
  `staging.tfplan`, is now protected alongside Terraform state and real tfvars.

## Resulting repository state

The configuration remains intentionally unapplied. It represents one Starter API
web service, one free PostgreSQL instance, one free Key Value instance, one Starter
private RabbitMQ service, and its nested 10 GiB persistent disk. Terraform will
show four root resource declarations because the RabbitMQ disk is a nested disk block,
but Render will create all five accepted platform resources.

At the current Render rates, the expected fixed baseline on a Hobby workspace is
approximately USD 16.50/month: USD 7 each for the API and RabbitMQ Starter services,
plus USD 2.50/month for the 10 GiB disk. PostgreSQL and Key Value use their free
plans. This excludes variable egress and pipeline-minute charges. Free PostgreSQL is
limited to 1 GiB, expires after 30 days, has a 14-day upgrade grace period before
deletion, and has no backups. Free Key Value is limited to 25 MB, 50 connections, and
does not persist through a restart.

No Terraform plan, apply, state file, Render resource, account credential, or image
credential was created. The temporary local Terraform working directory is ignored;
all repository changes are limited to the saved-plan ignore rule and this checkpoint.

## Verification

- Downloaded and used temporary Terraform 1.15.9; no system-wide Terraform
  installation was required.
- `terraform fmt -check` passed.
- `terraform init -backend=false` passed, using the locked official
  `render-oss/render` provider 1.9.1.
- `terraform validate` passed.
- `git diff --check` passed.
- Confirmed `.gitignore` now ignores `.terraform/`, `terraform.tfstate`,
  `staging.tfplan`, and real `*.tfvars` files.

## Decisions made

No architecture decision was made. The existing Render topology and its Terraform
provider version remain unchanged.

## Intentionally unresolved

- Render workspace/account, API key, and owner ID bootstrap.
- An immutable GHCR SHA image that has actually been published for the selected
  source revision.
- A Render GHCR registry credential ID if the GHCR package is private. Its GitHub
  token remains outside Terraform state.
- Locally supplied RabbitMQ password and Erlang cookie.
- The first authenticated Terraform plan, review/approval of its exact actions, and
  the first apply.
- Secure state retention beyond this single-machine first-deployment boundary.

## Next recommended step

Complete the external bootstrap locally without placing secrets in source control,
then run the first authenticated `terraform plan -out staging.tfplan`. Review the
complete plan and its cost-bearing resources before requesting explicit approval to
apply it.
