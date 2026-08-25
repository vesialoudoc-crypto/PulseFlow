# Checkpoint: Publish the API image to GitHub Container Registry

**Date:** 2026-08-25

## Starting point

Stage 4's local scaling/load milestone was complete and Stage 5 deployment was the
next active major stage. The repository had a multi-stage Dockerfile for
`PulseFlow.Api`, but GitHub Actions only ran validation; no workflow published a
versioned OCI image.

## What changed

- Confirmed that `src/PulseFlow.Api/Dockerfile` uses a .NET SDK build stage, publishes
  the API, removes `appsettings.Development.json`, and copies only the required
  `/app/publish` output into an ASP.NET runtime final stage. The existing `migrations`
  stage remains because the local Compose topology uses it.
- Added a dedicated GitHub Actions workflow that runs on pushes to `develop`, runs the
  existing repository checks, builds from the repository-root Docker context, and
  pushes the API image to GitHub Container Registry.
- The workflow authenticates with the built-in `GITHUB_TOKEN` and the minimum required
  `contents: read` and `packages: write` permissions.
- Documented the GHCR image name, immutable full-SHA tag, movable `develop` tag, pull
  command, and environment-agnostic configuration boundary in the Stage 5 roadmap.

## Resulting repository state

After a push to `develop`, the workflow publishes:

```text
ghcr.io/<repository-owner>/pulseflow-api:sha-<40-character-commit-sha>
ghcr.io/<repository-owner>/pulseflow-api:develop
```

`sha-<40-character-commit-sha>` is the immutable deployable/auditable reference.
`develop` is a movable convenience tag. The workflow never publishes `latest`.
`docker/metadata-action` normalizes the derived GHCR image name as required by the
registry.

The Docker build uses `context: .` and
`file: src/PulseFlow.Api/Dockerfile`, so its paths are resolved from the repository
root. The final image contains only the required API publish output; the
development-only `appsettings.Development.json` file is removed. PostgreSQL, RabbitMQ,
Redis, and other runtime configuration remain external to the image; no such values
are supplied by the publishing workflow.

## Verification

- `dotnet csharpier check .` passed: 54 files checked.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `pwsh ./scripts/test.ps1` passed: 85 unit tests and 57 integration tests passed.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `docker build --no-cache -f src/PulseFlow.Api/Dockerfile -t pulseflow-api:test .`
  passed.
- The resulting image contains `PulseFlow.Api.dll` and `appsettings.json`, has the
  `dotnet PulseFlow.Api.dll` entry point, and does not contain
  `appsettings.Development.json` or the local Compose PostgreSQL/RabbitMQ credentials.
- A temporary container with externally supplied test configuration returned HTTP 200
  from `GET /health/live`.
- `rhysd/actionlint:1.7.9 .github/workflows/publish-api-image.yml` passed with no
  findings.

## Decisions made

No new deployment architecture decision or ADR was required. GHCR was the explicitly
requested registry, and this step only publishes a portable application artifact. It
does not select a cloud platform, deployment topology, environment model, or release
strategy beyond the conservative `develop` publication trigger.

## Intentionally unresolved

- The future AWS/staging deployment architecture, configuration and secret storage,
  network topology, release strategy, and image-consumption mechanism.
- GHCR package visibility and any organization-level Actions package policy, which are
  configured in GitHub rather than committed to this repository.
- A deployed-environment performance measurement and subsequent bottleneck work.

## Next recommended step

Use the immutable SHA-tagged image as the input to a separately designed minimal
deployment architecture. Decide the target environment, runtime configuration/secret
delivery, and image-consumption mechanism before implementing them.
