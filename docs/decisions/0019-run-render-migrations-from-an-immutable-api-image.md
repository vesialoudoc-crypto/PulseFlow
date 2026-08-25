# ADR 0019: Run Render migrations from an immutable API image

**Date:** 2026-08-25

## Status

Accepted

## Context

The accepted Render staging rollout must apply pending EF Core migrations exactly
once before the selected immutable API version receives traffic. The API validates
migration currency at startup and must not migrate independently. The previously
published runtime image had no executable migration artifact, so it could not supply
the required Render pre-deploy command.

## Alternatives considered

1. Add an EF Core migration bundle to the same immutable image as `PulseFlow.Api`.
2. Publish a separate migration image derived from the same source revision.
3. Run migrations from normal API startup.

## Decision

Build a framework-dependent EF Core migration bundle at
`/app/migrations/pulseflow-migrations` and copy it into the final API image. Configure
Render pre-deploy to run:

```text
/app/migrations/pulseflow-migrations --connection "$ConnectionStrings__PulseFlow"
```

The selected API image therefore supplies both the migration executable and the
unchanged API entry point, `dotnet PulseFlow.Api.dll`. The PostgreSQL connection is
provided at Render runtime; no connection string is embedded in the image or
committed Terraform source.

## Consequences

- A selected immutable SHA image identifies both the code that migrates the schema
  and the code that starts after a successful pre-deploy phase.
- Render must complete the pre-deploy command before API rollout. API replicas retain
  their startup migration check and never execute migrations themselves.
- The runtime image does not include `dotnet-ef`; it includes only the generated
  framework-dependent bundle and the ASP.NET Core runtime required to execute it.
- The local Compose `migrations` stage remains SDK-based and unchanged in behavior.
- A separate migration image is not needed for the first staging slice, but can be
  reconsidered if later deployment requirements justify it.
