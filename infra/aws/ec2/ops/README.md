# AWS EC2 Operations

Terraform creates clean EC2 hosts, networking, and the instance IAM role. This
directory performs explicit AWS-specific bootstrap, transport, and inspection on
already-created hosts. Terraform never invokes these scripts.

`infra/runtime` defines the provider-independent service runtime. This directory
does not alter its Compose topology; it transports the reviewed runtime files to AWS
EC2 hosts through Systems Manager.

## Prerequisites

The scripts require AWS CLI v2, a local AWS credential context that can access
Frankfurt (`eu-central-1`), and the `AmazonSSMManagedInstanceCore` instance role
provisioned by the EC2 Terraform root. `connect.ps1` additionally needs the AWS
Session Manager plugin. No script uses Terraform state, Terraform output, SSH, S3,
Secrets Manager, or Parameter Store.

Each role is discovered through AWS CLI `ec2 describe-instances` using its exact
`Name` tag and the `running` state:

| Role | Name tag |
| --- | --- |
| `app` | `pulseflow-app` |
| `postgres` | `pulseflow-postgres` |
| `rabbitmq` | `pulseflow-rabbitmq` |
| `redis` | `pulseflow-redis` |

Discovery requires exactly one matching running instance and a private IP address.
It fails rather than choosing an ambiguous host.

## Operational sequence

```text
Terraform
  -> creates EC2, network, and IAM

bootstrap.ps1
  -> installs and starts Docker Engine plus the pinned Docker Compose v2 plugin
     through SSM Run Command

copy-runtime.ps1
  -> copies portable runtime configuration through SSM Run Command

future secret/config step
  -> not implemented

future deployment step
  -> docker compose up, not implemented
```

Bootstrap one host, or all four hosts:

```powershell
pwsh ./infra/aws/ec2/ops/bootstrap.ps1 app
pwsh ./infra/aws/ec2/ops/bootstrap.ps1 all
```

`bootstrap.ps1` sends the local `install-docker.sh` as Base64 over SSM Run Command,
executes it from a temporary remote file, waits for completion, and removes that
temporary file. The installer is idempotent: it ensures Docker Engine is enabled and
started, then installs the pinned `v2.32.4` Compose CLI plugin for x86_64 hosts at
`/usr/local/lib/docker/cli-plugins/docker-compose`. It verifies both `docker version`
and `docker compose version`; it does not install the legacy `docker-compose` command.

Copy a role's portable runtime files, or all four definitions:

```powershell
pwsh ./infra/aws/ec2/ops/copy-runtime.ps1 app
pwsh ./infra/aws/ec2/ops/copy-runtime.ps1 all
```

The destination is `/opt/pulseflow/runtime/<role>/`. The application host receives
`compose.yaml` and `haproxy.cfg`; every other host receives `compose.yaml`.
Transport uses Base64 into a temporary remote staging directory, then installs
readable files into the destination and removes the staging directory. Each copy
performs `docker compose config` remotely. Required Compose values use temporary
placeholder values in that command only; no runtime secret or `.env` file is created.

Inspect all four hosts:

```powershell
pwsh ./infra/aws/ec2/ops/status.ps1
```

The status output shows instance identity, private IP, SSM managed/online state,
Docker state when SSM is online, and whether the expected runtime directory exists.
Containers are intentionally not expected to run yet.

Open an interactive Session Manager shell on one host:

```powershell
pwsh ./infra/aws/ec2/ops/connect.ps1 app
pwsh ./infra/aws/ec2/ops/connect.ps1 rabbitmq
pwsh ./infra/aws/ec2/ops/connect.ps1 redis
pwsh ./infra/aws/ec2/ops/connect.ps1 postgres
```

## Deliberately deferred

This foundation does not create AWS resources, start containers, pull an image, run
migrations, create a `.env` file, or run `docker compose up`. It does not transmit
real PostgreSQL or RabbitMQ passwords, a GHCR token, or any other runtime secret.
Secret/config delivery and runtime deployment are separate future operations steps.
