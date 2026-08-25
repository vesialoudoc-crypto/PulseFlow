# Checkpoint: Make GitHub Actions workflows manual-only

**Date:** 2026-08-25

## Starting point

The repository contained an automatic CI workflow for pushes and pull requests, and
the API-image publication workflow automatically ran for every push to `develop`.

## What changed

- Replaced the automatic `push` and `pull_request` triggers in `ci.yml` with
  `workflow_dispatch`.
- Replaced the automatic push-to-`develop` trigger in `publish-api-image.yml` with
  `workflow_dispatch`.
- Preserved all existing workflow jobs and steps.
- Updated the Stage 5 roadmap to state that a developer manually starts the image
  workflow for an explicit staging-image release.

## Resulting repository state

Both **CI** and **Publish PulseFlow.Api image** remain available in GitHub Actions
through **Run workflow**, but neither starts automatically. Starting the publication
workflow validates the repository and then publishes the same immutable full-SHA tag
and movable `develop` tag to GitHub Container Registry as before.

## Verification

- `dotnet csharpier check .` passed: 54 files checked.
- `dotnet build PulseFlow.slnx -warnaserror` passed with 0 warnings and 0 errors.
- `pwsh ./scripts/test.ps1` passed: 85 unit tests and 57 integration tests passed.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `rhysd/actionlint:1.7.9 .github/workflows/ci.yml
  .github/workflows/publish-api-image.yml` passed with no findings.

## Decisions made

The automatic workflow triggers were disabled as an explicit cost-control measure.
Manual dispatch is sufficient for the current developer-initiated CI and staging-image
release workflow. No deployment architecture or release-tagging strategy changed.

## Intentionally unresolved

- The future AWS/staging deployment architecture, configuration and secret storage,
  network topology, release strategy, and image-consumption mechanism.
- Whether automatic checks should be re-enabled later remains a future cost and
  delivery-process decision.

## Next recommended step

When a staging image is needed, manually run **Publish PulseFlow.Api image** from
GitHub Actions and use its immutable SHA tag for downstream deployment work.
