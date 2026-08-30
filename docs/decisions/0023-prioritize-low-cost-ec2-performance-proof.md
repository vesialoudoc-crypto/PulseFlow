# ADR 0023: Prioritize a low-cost EC2 performance proof

**Date:** 2026-08-27

## Status

Superseded by [ADR 0025](0025-separate-ec2-provisioning-from-runtime-operations.md).

This remains the historical record of the cost reduction applied to the retired
five-node coupled EC2 implementation.

## Context

ADR 0022 selected four `m7i.large` nodes and one `t3.small` node to reduce
T-family credit behavior and Flex CPU-performance scaling in the primary experiment
inputs. The first real apply created only Redis before the account rejected the four
`m7i.large` launches. No bootstrap, deployment, smoke, persistence, or performance
result exists from that attempt.

The AWS environment is for deployment practice, distributed-topology proof, smoke
validation, and short controlled load runs. Long sustained performance experiments
belong to the local/home performance environment. The earlier sizing therefore
over-optimized benchmark isolation and did not respect the primary AWS cost
constraint.

The current image is x86_64 only. Before accepting the new application-node type,
the x86_64 architecture and requested `c7i-flex.large` shape were inspected:
`c7i-flex.large` was offered in `eu-central-1a` and reported 2 vCPUs and 4 GiB. An EC2
`RunInstances` dry-run returned `DryRunOperation` for that request. The dry-run did
not create an instance, so an actual `c7i-flex.large` launch remains unproven until a
future reviewed apply.

## Alternatives considered

1. Retain the four `m7i.large` nodes for a cleaner long-duration benchmark baseline.
   This does not fit the short-lived AWS purpose or its cost constraint.
2. Collapse every runtime component onto one EC2 instance. This reduces cost further
   but loses the required distributed deployment topology.
3. Use a Graviton topology. This is not viable until the GHCR image is published for
   ARM64.
4. Use one `c7i-flex.large` application node and four `t3.small` supporting nodes.
   This preserves the five-node topology while minimizing the intended short-run cost.

## Decision

Keep the five-node, single-AZ EC2 topology from ADR 0022, with root-only encrypted
gp3 storage, private service traffic, public IPv4 only for outbound bootstrap/SSM,
no public inbound access, no SSH, no bastion, no NAT Gateway, no ALB, and no Elastic
IP.

Replace the instance sizing with:

- `app`: `c7i-flex.large` (2 vCPU, 4 GiB) running HAProxy and two
  `PulseFlow.Api` containers;
- `rabbitmq`: `t3.small` (2 vCPU, 2 GiB);
- `redis`: `t3.small` (2 vCPU, 2 GiB);
- `postgres`: `t3.small` (2 vCPU, 2 GiB); and
- `loadgen`: `t3.small` (2 vCPU, 2 GiB).

Allocate only root gp3 volumes: 16 GiB for app, 16 GiB for RabbitMQ, 8 GiB for
Redis, 32 GiB for PostgreSQL, and 8 GiB for load generation, for 80 GiB in total.
The sizes cover Amazon Linux, Docker and container layers, plus a bounded PostgreSQL
data/WAL allowance and RabbitMQ state for a short proof. They are not a capacity
claim or a storage benchmark result.

Keep the Terraform Scheduler definition because the scheduled start/stop policy
remains architecturally correct. Scheduler authorization is an operator permission
issue, not a reason to redesign the runtime.

## Consequences

- AWS costs now favor a short deployment and smoke-proof lifecycle over clean
  long-duration benchmark inputs. T-family credit behavior and Flex CPU scaling are
  accepted variables in this temporary cloud environment.
- Any sustained or attribution-sensitive performance experiment must run in the
  local/home environment until a separately justified cloud measurement design exists.
- The root-only allocation falls from the prior 124 GiB proposal to 80 GiB. Root EBS
  and runtime Secrets Manager storage still cost money while instances are stopped.
- The x86_64/requested `c7i-flex.large` shape was inspected and its EC2
  `RunInstances` dry-run returned `DryRunOperation`; neither result proves an actual
  launch. The prior Redis creation proves `t3.small` can launch in the same account
  and region. A `c7i-flex.large` launch remains unproven until a future reviewed apply
  after the old environment is fully removed.
- No Scheduler resource is created by this decision. The current principal remains
  unable to create or list Scheduler groups, so a future apply may still fail there.

## Intentionally deferred

- Full cleanup of the failed partial environment and a new real Terraform plan.
- A complete EC2 bootstrap, deployment, smoke, RabbitMQ-consumer, PostgreSQL
  persistence, and cleanup proof.
- Long-duration cloud benchmarks, capacity conclusions, performance targets, and
  a bottleneck attribution claim.
