# ADR 0024: Minimize one-time EC2 proof storage

**Date:** 2026-08-27

## Status

Superseded by [ADR 0025](0025-separate-ec2-provisioning-from-runtime-operations.md).

This remains the historical record of the final storage and scheduling change to the
retired coupled EC2 implementation.

## Context

ADR 0023 reduced the temporary EC2 topology to one `c7i-flex.large` application node
and four `t3.small` supporting nodes, but its 80 GiB root-only allocation still
included discretionary short-proof headroom. The current task is one manually stopped
deployment proof, not a sustained cloud benchmark or a production storage exercise.

The selected AL2023 x86_64 AMI (`ami-02d33cbf17ed94bf3`) has one 8 GiB gp3 root
snapshot. The active AWS principal cannot create EventBridge Scheduler resources, and
the one-time proof does not need a recurring start/stop policy.

## Alternatives considered

1. Retain 80 GiB for Docker and service-state headroom. This adds recurring EBS cost
   without supporting the one-time proof objective.
2. Reduce every root below the AMI snapshot size. EC2 cannot create a root volume
   smaller than its source snapshot.
3. Remove Scheduler support from Terraform. This would discard an accepted recurring
   environment capability because of a temporary operator authorization limitation.
4. Use AMI-minimum roots except a small PostgreSQL allowance, and disable Scheduler
   only in this saved proof plan. This satisfies the proof objective while preserving
   the reusable design.

## Decision

Use encrypted root-only gp3 volumes of 8 GiB for app, RabbitMQ, Redis, and load
generation, plus 16 GiB for PostgreSQL: 48 GiB in total. Every root is at least the
selected AMI snapshot's 8 GiB minimum. No production storage headroom is included.

Create the one-time saved plan with the existing
`enable_business_hours_schedule = false` input. This omits the Scheduler group and
the start/stop schedules. The variable's default remains `true`; Scheduler support is
not removed from the Terraform design. The proof lifecycle stops nodes manually after
validation.

## Consequences

- The saved plan falls from 41 to 38 resources: the Scheduler group and two schedules
  are absent. The existing Scheduler IAM role and inline policy remain because they
  are not conditional on the feature input; they have no direct service charge.
- Root EBS storage falls from 80 GiB to 48 GiB. EBS storage still accrues while the
  instances are stopped.
- The smaller volumes are only appropriate for the short proof. A storage-pressure
  failure is evidence to record, not authorization to add unreviewed production
  capacity.

## Intentionally deferred

- A future recurring environment may enable Scheduler after the required AWS
  authorization is available.
- Any longer cloud benchmark, storage capacity claim, or production disk design.
