# Checkpoint: Add Render staging Terraform definition

**Date:** 2026-08-25

## Starting point

Render was the accepted disposable staging platform, and GitHub Actions could publish
an immutable GHCR API image. No Render resource definition or migration artifact
existed. The API runtime image did not contain a migration bundle, so it could not
execute the accepted Render pre-deploy migration lifecycle.

## What changed

- Added the first flat Terraform configuration in `infra/render/`, using the official
  `render-oss/render` provider.
- Modelled the public API web service, managed PostgreSQL, managed Key Value, private
  RabbitMQ service, and RabbitMQ persistent disk.
- Configured API readiness at `/health/ready`, one managed API instance, and a
  required immutable full-SHA GHCR image input.
- Wired managed PostgreSQL and Key Value internal connection strings and a private
  RabbitMQ AMQP connection to the existing application configuration keys.
- Added an EF Core migration bundle to the final immutable API image and configured
  the Render pre-deploy command to run it using the runtime PostgreSQL connection.
- Documented sensitive-input, private-GHCR credential bootstrap, Terraform-state,
  cost, and operator-access boundaries.
- Recorded the immutable-image migration-bundle decision in ADR 0019.

## Resulting repository state

The repository has a reviewable, un-applied Render staging configuration. Its default
topology is one paid Starter API service, free PostgreSQL, free disposable Key Value,
and one paid Starter private RabbitMQ service with a 10 GiB disk. PostgreSQL and Key
Value are private-only unless an external trusted operator CIDR is deliberately
provided. RabbitMQ AMQP `5672` remains private.

The final API image now contains:

```text
dotnet PulseFlow.Api.dll
/app/migrations/pulseflow-migrations
```

Render will run the migration bundle only during pre-deploy, then start the normal
image entry point after success. No real Render resources, credentials, state backend,
or deployment automation were created.

## Verification

- `terraform fmt -check` passed with Terraform 1.15.9.
- `terraform init -backend=false` passed and installed the locked official
  `render-oss/render` provider 1.9.1 without configuring a backend.
- `terraform validate` passed.
- `docker build --file src/PulseFlow.Api/Dockerfile --target final --tag
  pulseflow-api:iac-verification .` passed. A container check confirmed both
  `/app/PulseFlow.Api.dll` and executable `/app/migrations/pulseflow-migrations`;
  the bundle accepted `--help` in the final ASP.NET runtime image.
- `dotnet csharpier check .` passed: 54 files checked.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `pwsh ./scripts/test.ps1` passed: 85 unit and 57 integration tests.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `git diff --check` passed with no whitespace errors.

## Decisions made

- Terraform is the single staging IaC definition; a Render Blueprint was considered
  but not added because future IaC will extend into AWS.
- The migration bundle is part of the same immutable API image. See
  [ADR 0019](../decisions/0019-run-render-migrations-from-an-immutable-api-image.md).

## Intentionally unresolved

- Render account/API-key/owner bootstrap, secure state storage, and first plan/apply.
- A Render GHCR registry credential bootstrap for a private image. Its token is not
  managed by Terraform because it would enter state.
- Trusted operator CIDRs, current Render pricing review, and a protected RabbitMQ
  management-UI access path. The provider cannot cleanly expose only that UI from the
  private RabbitMQ service, so it remains deferred.
- Automatic deployment/CI-CD, API/consumer decoupling, RabbitMQ HA/backup/prefetch,
  deployed load testing, and final AWS architecture.

## Next recommended step

Install or obtain a local Terraform binary, validate the configuration without a
backend, then perform a controlled first plan only after the Render account, registry
credential, sensitive inputs, and state-storage boundary are ready for review.
