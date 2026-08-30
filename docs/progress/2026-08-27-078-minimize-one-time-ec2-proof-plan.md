# Checkpoint: Minimize one-time EC2 proof plan

**Date:** 2026-08-27

## Starting point

Checkpoint 077 recorded that the failed EC2 performance environment had been fully
removed and Terraform state was empty. A fresh 41-resource saved plan then represented
the accepted five-node low-cost topology with 80 GiB of encrypted root-only gp3
storage and an enabled EventBridge Scheduler group plus two schedules. No apply or
runtime deployment had occurred.

## What changed

- Queried the selected AL2023 x86_64 AMI `ami-02d33cbf17ed94bf3`. Its only root
  snapshot is an 8 GiB gp3 volume.
- Reduced encrypted root-only gp3 allocation to 8 GiB for app, RabbitMQ, Redis, and
  load generation, and 16 GiB for PostgreSQL: 48 GiB total. Every volume meets or
  exceeds the selected AMI snapshot minimum; no production storage headroom was added.
- Created the one-time saved plan with the existing
  `enable_business_hours_schedule = false` input. Terraform retains Scheduler support
  with its default enabled, but this plan omits the Scheduler group and schedules
  because the principal cannot create them and the proof is manually stopped after
  validation.
- Replaced the prior saved plan with the new plan. No AWS resource was applied or
  deployed.

## Resulting repository and AWS state

The saved plan in `infra/aws-ec2-performance/` creates 38 resources. Its five EC2
instances retain the accepted types and use only encrypted, delete-on-termination gp3
roots totaling 48 GiB. It creates no Scheduler group or schedule. The unconditional
Scheduler IAM role and inline policy remain in the plan; they have no direct service
charge and do not create Scheduler resources.

Terraform state remains empty. AWS resource state is unchanged because planning did
not apply infrastructure.

## Verification

- `aws ec2 describe-images --region eu-central-1 --image-ids ami-02d33cbf17ed94bf3`
  reported an available AL2023 x86_64 image with a single `/dev/xvda` gp3 snapshot of
  8 GiB.
- `terraform -chdir=infra/aws-ec2-performance fmt -recursive` completed successfully.
- `terraform -chdir=infra/aws-ec2-performance validate` passed.
- The lifecycle planning command completed with an explicit AL2023 AMI override and
  `TF_VAR_enable_business_hours_schedule=false`: `Plan: 38 to add, 0 to change, 0 to
  destroy.`
- Saved-plan JSON comparison against the preceding plan found no added addresses and
  exactly three removed addresses: the Scheduler group and start/stop schedules. It
  confirmed the five root sizes total 48 GiB and retain gp3, encryption, and
  delete-on-termination.

## Decisions made

- The one-time proof uses the minimum reviewed storage allocation and disables
  Scheduler only through its existing feature input. See [ADR 0024](../decisions/0024-minimize-one-time-ec2-proof-storage.md).
- The previous plan is superseded. No apply, deployment, commit, or push is
  authorized by this checkpoint.

## Intentionally unresolved

- The active principal cannot independently read or create Scheduler resources. A
  later recurring environment requires that authorization before enabling the feature.
- Actual EC2 bootstrap, runtime deployment, smoke validation, and cleanup proof
  remain unperformed.

## Next recommended step

Review the 38-resource saved plan and current AWS pricing. Apply only after explicit
approval, then stop the nodes immediately after the approved validation path.
