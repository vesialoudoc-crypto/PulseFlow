# AWS EC2 Provisioning

This root creates a deliberately small, disposable EC2 infrastructure layer. It is
separate from the established managed AWS proof in [`../managed-legacy/`](../managed-legacy/), which this
directory does not modify.

Terraform creates only a dedicated VPC, one public subnet with Internet egress,
four security groups, a minimal SSM instance role/profile, and four clean Amazon
Linux 2023 x86_64 EC2 hosts:

- `pulseflow-app`
- `pulseflow-rabbitmq`
- `pulseflow-redis`
- `pulseflow-postgres`

All hosts default to `t3.small` with one 8-GiB encrypted gp3 root volume. There are
no separate data disks, load-generator host, NAT gateway, load balancer, scheduler,
secrets, SSH ingress, user-data, Docker installation, or deployment automation.

## Boundary

Terraform provisioning ends when the four clean hosts are available through AWS
Systems Manager Session Manager. It never invokes the operations layer.

[`ops/`](ops/) contains
the explicit SSM connection helper and a standalone Docker installation script for
already-created hosts. Runtime deployment is intentionally deferred.

## Network rules

The VPC has one public subnet so instances can use auto-assigned public IPv4 for
outbound access. There is no public SSH ingress. Service traffic uses private VPC
networking only:

- `allowed_http_source_cidr` → app TCP 80;
- app security group → RabbitMQ TCP 5672;
- app security group → Redis TCP 6379; and
- app security group → PostgreSQL TCP 5432.

`allowed_http_source_cidr` is required and rejects `0.0.0.0/0`. Set it to the narrow
public CIDR from which future external test traffic will originate. HTTP port 80 is
prepared for that later workload; this root does not install an HTTP service.

## Safe local checks

Supply the required HTTP source CIDR and explicitly selected Amazon Linux 2023 x86_64
AMI ID through a local, uncommitted variable file or command-line variables, then run:

```powershell
terraform -chdir=infra/aws/ec2 init -backend=false
terraform -chdir=infra/aws/ec2 fmt -check
terraform -chdir=infra/aws/ec2 validate
```

This root does not resolve an AMI through AWS Systems Manager Parameter Store and does
not require `ssm:GetParameter`. Review any plan and current AWS prices before a
separately approved apply; this task neither applies nor destroys infrastructure.
