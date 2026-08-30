# Checkpoint: Record successful EC2 lifecycle proof

**Date:** 2026-08-31

## Starting point

The active Stage 5 roadmap and EC2 architecture documentation described the EC2
provisioning and portable runtime layers, but still stated that EC2 host operations,
runtime deployment, migrations, and smoke validation were future work. The real EC2
proof had since completed successfully on branch
`feature/stage5-ec2-low-cost-cleanup`.

## What changed

- Corrected the Stage 5 roadmap to distinguish the historical managed-AWS proof from
  the now-proven disposable EC2 lifecycle.
- Corrected the EC2 architecture document to record the successful runtime deployment
  while retaining the Terraform-only infrastructure boundary.
- Recorded that the proof created and later destroyed 16 Terraform resources and used
  four Amazon Linux 2023 `t3.small` instances for app, PostgreSQL, RabbitMQ, and
  Redis.

## Resulting repository and AWS state

Terraform apply created 16 resources. All four hosts became reachable through AWS
Systems Manager. Docker Engine and Docker Compose were installed and running, and the
provider-independent runtime definitions were copied to their corresponding hosts.

PostgreSQL, RabbitMQ, and Redis containers started successfully. The app host ran
HAProxy, two PulseFlow API replicas, and the one-shot EF migration bundle. The bundle
required an explicit `--connection` argument; after that correction, migrations
completed successfully.

Internal readiness through HAProxy returned HTTP 200 with `Healthy`, and the external
EC2 public app endpoint also returned `Healthy`. A real external NDJSON request to
`POST /api/events` returned HTTP 202 Accepted. The exact event was found in
PostgreSQL, proving the deployed ingestion path reached persistence; the smoke-test
database row was removed afterward.

Terraform destroy completed with `Destroy complete! Resources: 16 destroyed.`
`terraform state list` returned empty afterward. The disposable EC2 environment no
longer exists.

## Verification

- Documentation-only correction reviewed against the recorded successful EC2 proof.
- No Terraform, AWS CLI, SSM, runtime, application, test, or infrastructure command
  was run for this documentation update.

## Decisions made

No architecture or automation decision changed. Terraform still owns AWS
infrastructure only; `infra/aws/ec2/ops` owns AWS-specific host/bootstrap/transport
operations, and `infra/runtime` remains provider-independent. The managed AWS proof
remains historical evidence through Git history, ADRs, pull requests, and historical
checkpoints, but its implementation is no longer present in the active repository
tree.

## Intentionally unresolved

- A runtime secret-delivery mechanism and an automated deployment pipeline.
- Deployed observability, k6/performance measurement, bottleneck investigation, and
  before/after optimization.
- Production networking, high availability, backups, and final production topology.

## Next recommended step

Define the next small deployed-observability or performance-measurement slice without
claiming results before a new disposable EC2 lifecycle is measured and recorded.
