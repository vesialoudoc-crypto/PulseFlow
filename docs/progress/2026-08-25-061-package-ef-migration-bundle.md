# Checkpoint: Package the EF Core migration bundle in the API image

**Date:** 2026-08-25

## Starting point

The Render staging architecture and its pre-rollout migration invariant were accepted.
`PulseFlow.Api` was published as one immutable GHCR image, but the final image did
not contain a migration runner. The Dockerfile's `migrations` stage existed solely
for local Compose, where it runs `dotnet ef database update` before the API starts.

## What changed

- Built a framework-dependent Linux EF Core migration bundle in the existing Docker
  `migrations` stage and copied it to the final API image at
  `/app/migrations/pulseflow-migrations`.
- Preserved the final image entry point as `dotnet PulseFlow.Api.dll` and preserved
  the local Compose `migrations` target and its `dotnet ef database update` command.
- Accepted the EF Core bundle as the Render pre-deploy migration mechanism in
  [ADR 0019](../decisions/0019-use-ef-core-migration-bundle-in-api-image.md).
- Updated staging and Stage 5 documentation with the exact future pre-deploy command:

  ```text
  /app/migrations/pulseflow-migrations --connection "$ConnectionStrings__PulseFlow"
  ```

## Resulting repository state

One immutable API image now contains both executables from the same source revision:

```text
pulseflow-api:sha-<commit-sha>
    ├─ dotnet PulseFlow.Api.dll
    └─ /app/migrations/pulseflow-migrations
```

Render will supply `ConnectionStrings__PulseFlow` as a runtime secret and execute the
bundle explicitly in its pre-deploy lifecycle. Normal API startup does not run
migrations, and no second GHCR migration image is published. EF Core migration history
makes an already-current database a successful no-op. The controlled pre-deploy
execution meaning of "exactly once" is operational, not a mathematical database
guarantee.

## Verification

- `dotnet csharpier check .` passed: 54 files checked.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `pwsh ./scripts/test.ps1` passed: 85 unit tests and 57 integration tests passed.
- `docker build -f src/PulseFlow.Api/Dockerfile -t pulseflow-api:migration-test .`
  passed.
- Final-image inspection confirmed the entry point is
  `["dotnet","PulseFlow.Api.dll"]`, the migration bundle is executable at
  `/app/migrations/pulseflow-migrations`, `dotnet-ef` is absent, and neither
  `appsettings.Development.json` nor the local Compose password is present in
  `/app`.
- Against an isolated temporary PostgreSQL container, the bundle applied both
  committed migrations and exited 0; `__EFMigrationsHistory` contained 2 rows.
- The documented Render-style command, with `ConnectionStrings__PulseFlow` expanded
  by `/bin/sh`, applied both migrations to a separate isolated PostgreSQL container
  and exited 0.
- A second run against that database printed that it was already up to date and exited
  0.
- A run with `Host=127.0.0.1;Port=1` failed to connect and exited 1.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `git diff --check` passed with no whitespace errors.

## Decisions made

- Use an EF Core migration bundle within the immutable API image rather than a
  separate migration OCI image or API-startup migration execution. See
  [ADR 0019](../decisions/0019-use-ef-core-migration-bundle-in-api-image.md).

## Intentionally unresolved

- Render resources, pre-deploy configuration, runtime secret creation, and actual
  staging deployment.
- Terraform/OpenTofu, automatic staging deployment, and operator runbooks.
- Final AWS architecture, independent API/consumer scaling, and deployed load
  measurement.

## Next recommended step

Implement the accepted Render infrastructure and configure the immutable GHCR image,
`ConnectionStrings__PulseFlow` secret, `/health/ready`, and documented pre-deploy
migration command without changing the image's normal API entry point.
