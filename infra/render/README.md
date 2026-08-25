# Render staging Terraform

This directory is the single declarative definition of the accepted Render staging
topology. It uses the official `render-oss/render` Terraform provider rather than a
Render Blueprint. Blueprints were considered, but Terraform was selected because
PulseFlow intends to manage infrastructure beyond Render during the later AWS stage.
The configuration uses ordinary HCL supported by Terraform and OpenTofu; it does not
create a second Blueprint source of truth.

`versions.tf` requires Terraform 1.6 or later and constrains the Render provider to
the 1.9.1 patch line. `terraform init` records the selected provider checksum in the
committed `.terraform.lock.hcl` file.

## Resources represented

- public `render_web_service` for `PulseFlow.Api`, with Render HTTPS ingress,
  `/health/ready`, one managed API instance, and an immutable GHCR SHA image;
- `render_postgres` for the durable staging event store, using its internal
  connection string;
- `render_keyvalue` for disposable Redis-compatible rate-limit state, with
  persistence explicitly disabled;
- private `render_private_service` for RabbitMQ 4.1.0 management image, with a
  10 GiB persistent disk at `/var/lib/rabbitmq`.

The API has no `start_command`, so Render retains the image's normal
`dotnet PulseFlow.Api.dll` entry point. Its pre-deploy command is:

```text
/app/migrations/pulseflow-migrations --connection "$ConnectionStrings__PulseFlow"
```

The bundle and API executable are in the same immutable image. Render runs the
pre-deploy command with the internally supplied PostgreSQL connection string before
starting that image as the API. Normal API startup only verifies migration currency;
it does not execute migrations.

## Inputs and secrets

`pulseflow_image` is required and must be an immutable full-SHA image tag. The
configuration splits it internally only because the Render provider's image schema
requires `image_url` and `tag` separately.

Set Render authentication outside source control:

```powershell
$env:RENDER_API_KEY = '<Render API key>'
$env:RENDER_OWNER_ID = '<Render user or team owner ID>'
```

`rabbitmq_password` and `rabbitmq_erlang_cookie` are required sensitive variables.
Use `TF_VAR_rabbitmq_password` and `TF_VAR_rabbitmq_erlang_cookie`, a secure local
secret manager, or a non-committed `.tfvars` file. Terraform also receives managed
PostgreSQL and Key Value connection strings and the RabbitMQ password while building
Render environment variables, so its state can contain sensitive infrastructure
values. Store state securely and do not commit it.

For a private GHCR package, first create a Render GHCR registry credential through a
controlled Render-account bootstrap step, then provide its non-secret Render ID as
`ghcr_registry_credential_id`. The provider supports creating registry credentials,
but doing so requires the GHCR token as Terraform input and would retain it in state.
This configuration intentionally does not manage that token or credential resource.
Set the ID to `null` only for a public image.

`trusted_operator_cidrs` is empty by default. That keeps PostgreSQL and Key Value
private-only; an explicit trusted CIDR can later allow restricted external operator
access. The provider does not provide a clean way to expose only the RabbitMQ
management UI from a private service while retaining private AMQP. RabbitMQ operator
UI access is therefore deferred; AMQP `5672` remains private.

## Plans and cost boundary

The defaults intentionally select the smallest topology compatible with the accepted
requirements: `starter` API service because Render pre-deploy commands require a
paid web service; `starter` RabbitMQ private service because persistent disks require
a paid service; its 10 GiB disk is Render's documented RabbitMQ layout. PostgreSQL
and Key Value use their free plans. Free PostgreSQL expires after 30 days, and free
Key Value is in-memory only; this is suitable only for the disposable personal
staging boundary and does not replace a backup or production plan.

An eventual apply creates cost-bearing API and RabbitMQ Starter services, RabbitMQ
persistent-disk storage, and any platform usage beyond free-plan allowances. Review
current Render pricing and workspace limits before the first plan/apply.

## First controlled plan

No Render resources have been created by this configuration. From this directory:

```powershell
terraform fmt -check
terraform init -backend=false
terraform validate

# After setting all external inputs and choosing secure state storage:
terraform plan -out staging.tfplan
```

Do not run `terraform apply` until a Render owner/workspace, API key, private-image
credential (if required), sensitive RabbitMQ values, state-storage approach, and any
trusted operator CIDRs have been reviewed. The initial plan should also confirm that
the selected Render account permits the chosen plans and region.
