# AWS EC2 implementation reference

The active AWS Terraform root is `infra/aws/ec2`. It creates only the disposable infrastructure layer; a successful apply does not start PulseFlow.

## Provisioned resources

Terraform creates a dedicated VPC, one public subnet and Internet route, four security groups, an EC2 role/profile with `AmazonSSMManagedInstanceCore`, and four clean Amazon Linux 2023 x86_64 hosts:

| Host | Default type | Runtime role |
| --- | --- | --- |
| `pulseflow-app` | `t3.small` | HAProxy and two API containers |
| `pulseflow-postgres` | `t3.small` | PostgreSQL |
| `pulseflow-rabbitmq` | `t3.small` | RabbitMQ |
| `pulseflow-redis` | `t3.small` | Redis |

Each host has one 8-GiB encrypted gp3 root volume and an auto-assigned public IPv4 address. The public address supports outbound host setup; SSM is the supported operator path.

## Network boundary

- The default `allowed_http_source_cidr` value, `0.0.0.0/0`, can reach the app host on TCP 80 and exposes that HTTP port publicly. A narrower CIDR is optional.
- The app security group can reach RabbitMQ on TCP 5672, Redis on TCP 6379, and PostgreSQL on TCP 5432.
- There is no public SSH ingress. PostgreSQL, RabbitMQ, and Redis accept service traffic only from the app security group through the private VPC. There is no NAT gateway, load balancer, Elastic IP, separate data disk, or load-generator host.

The AMI is an explicit input; Terraform does not resolve it through AWS Systems Manager Parameter Store.

## Terraform and runtime boundary

Terraform provisions clean hosts, networking, and the SSM role only. It does not install Docker, create or deliver runtime secrets, pull images, run migrations, start containers, run smoke tests, or invoke the operations scripts.

The AWS-specific [operations layer](ops/README.md) bootstraps and accesses the created hosts through SSM. The provider-independent [runtime definitions](../../runtime/README.md) define the Compose projects that run on them.

## Proven scope and limitations

One disposable lifecycle created 16 Terraform resources, deployed the four-host runtime, passed readiness and an end-to-end ingestion smoke test, and was destroyed successfully. It did not include k6 traffic, performance measurement, deployed observability, automated deployment, secret delivery, high availability, backups, or production networking.
