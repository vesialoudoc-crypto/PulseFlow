# Checkpoint: Restore automatic CI triggers

**Date:** 2026-09-07

## Starting point

The repository's CI and Docker image publication workflows both required manual
dispatch. The historical checkpoint documenting that earlier cost-control decision
remains unchanged.

## What changed

- Restored the **CI** workflow triggers for every `push` and `pull_request`.
- Kept the **Publish PulseFlow.Api image** workflow manually triggered with
  `workflow_dispatch` to control GitHub Actions usage and cost.
- Updated the active roadmap to distinguish automatic CI from manual image
  publication and to avoid implying automatic deployment or CD.

## Resulting repository state

GitHub Actions CI now runs automatically on pushes and pull requests while preserving
all existing restore, build, test, and formatting jobs and steps. Docker image
publication remains an explicit manual action. No application, infrastructure,
runtime, deployment, or publication behavior changed.

## Verification

- Final workflow trigger inspection confirmed automatic `push` and `pull_request`
  triggers for CI and manual `workflow_dispatch` for image publication.
- `git diff --check` passed.
- `pwsh ./scripts/check-project-docs.ps1` passed.
- `actionlint` was not available in the local environment, so it was not run.

## Decisions made

No architectural or deployment decision changed. This targeted restoration does not
introduce automatic deployment or CD, so no ADR was required.

## Intentionally unresolved

- Automated deployment/CD remains pending.
- Docker image publication remains manual by design.

## Next recommended step

Use the automatic CI checks on the next push or pull request; continue to publish
Docker images only through an intentional manual workflow dispatch.
