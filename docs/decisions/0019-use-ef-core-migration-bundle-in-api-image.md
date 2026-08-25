# ADR 0019: Use an EF Core migration bundle in the API image

**Date:** 2026-08-25

## Status

Accepted

## Context

Render staging deploys an immutable `PulseFlow.Api` GHCR image and needs pending EF
Core migrations to run before the selected revision receives traffic. Normal API
startup deliberately verifies migration state but never applies migrations, so API
replicas cannot independently participate in schema changes. Local Docker Compose
already uses the Dockerfile's `migrations` target and `dotnet ef database update`.

The deployment artifact must contain migration logic from the same source revision as
the API, obtain PostgreSQL access only at runtime, and not require `dotnet-ef` on the
Render host. The accepted lifecycle is one controlled migration execution before
rollout, followed by normal API startup.

## Alternatives considered

1. Build an EF Core migration bundle in the existing immutable `PulseFlow.Api` image
   and run it explicitly during Render pre-deploy.
2. Publish a separate migration OCI image derived from the same source revision.
3. Run `dotnet ef database update` during every API container startup.

## Decision

Build a framework-dependent Linux EF Core migration bundle in the Dockerfile's
existing `migrations` stage. Copy it to the final `PulseFlow.Api` image at:

```text
/app/migrations/pulseflow-migrations
```

Retain the final image's normal entry point:

```text
dotnet PulseFlow.Api.dll
```

Render will explicitly execute this pre-deploy command using the selected immutable
image:

```text
/app/migrations/pulseflow-migrations --connection "$ConnectionStrings__PulseFlow"
```

`ConnectionStrings__PulseFlow` is supplied as a runtime secret and expanded by the
pre-deploy command shell. The bundle receives the resulting connection string through
its standard `--connection` option. No PostgreSQL credential, Render endpoint, or
staging configuration is included in the image.

The existing local Compose `migrations` target remains unchanged. It continues to run
`dotnet ef database update` from the same Dockerfile stage rather than depending on
the final-image bundle.

## Consequences

- One immutable `ghcr.io/<repository-owner>/pulseflow-api:sha-<commit-sha>` image
  contains both the API and migration runner. No second GHCR repository or image is
  published.
- The migration runner is built from the same source revision as the API output and
  does not depend on `dotnet-ef` being installed where it executes.
- Migration execution is a controlled Render pre-deploy action. API startup and every
  API replica continue to verify only that the database is current.
- EF Core migration history determines whether a migration has already run. A bundle
  invocation against an already-current database succeeds without schema changes.
  This operational lifecycle meaning is not a claim of globally mathematically
  exactly-once database semantics.
- The final image grows by the migration bundle and its dependencies. This is accepted
  to keep the deployable API revision and its migration executable inseparable.

## Intentionally deferred

- Render resources and pre-deploy configuration, including secret creation and actual
  service deployment.
- Terraform/OpenTofu and automatic staging deployment.
- Independent API and RabbitMQ-consumer scaling, RabbitMQ HA/backup/prefetch, and
  deployed load measurements.
- Final AWS service selection and production rollout strategy.
