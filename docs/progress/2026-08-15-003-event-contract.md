# Checkpoint: Event ingestion envelope v1 accepted

**Date:** 2026-08-15

## Starting point

The repository contained a buildable ASP.NET Core solution skeleton with no application endpoints or tests. Stage 1 had not started, and the event format, payload boundary, HTTP endpoint, response behavior, and persistence design were unresolved.

## What changed

- Added the human-readable v1 event-envelope contract in `docs/contracts/event-ingestion-v1.md`.
- Fixed the required envelope fields as `type`, `source`, `occurredAt`, and `payload`.
- Selected an arbitrary JSON object, rather than any JSON value, as the top-level `payload` shape.
- Defined PulseFlow as payload-agnostic: sender-specific payload fields are neither interpreted nor validated by the ingestion boundary.
- Defined the relationship between the human-readable contract and the future runtime OpenAPI description.
- Marked Stage 1 as in progress and updated the documentation map and remaining Source of Truth questions.

## Resulting state

The repository now contains an accepted external request-envelope contract for implementation. It defines required field types, UTC timestamp representation, payload semantics, valid examples, and invalid top-level payload shapes.

The API still exposes no PulseFlow endpoints. There is no request model, validation code, generated OpenAPI operation, persistence implementation, or automated contract test. The contract document is therefore a target promise, not a claim about current runtime behavior.

## Verification

Run from the repository root:

```powershell
dotnet build PulseFlow.slnx
dotnet test PulseFlow.slnx
pwsh ./scripts/check-project-docs.ps1
```

Results:

- the solution builds with no warnings or errors;
- the test command succeeds and reports no discoverable tests in either test project;
- the documentation check completes without warnings;
- active documentation entry points consistently direct future sessions to the most recent checkpoint.

## Decisions made

- The v1 event envelope contains required `type`, `source`, `occurredAt`, and `payload` properties.
- `type` and `source` are required non-empty strings.
- `occurredAt` is a required RFC 3339 UTC timestamp serialized with `Z`.
- `payload` is a required arbitrary JSON object; an empty object is valid, while arrays, scalars, and `null` are invalid at its top level.
- PulseFlow persists payload as a single JSON value and does not introduce sender-specific payload models or domain validation.
- Human-readable contract documentation defines client-facing semantics; runtime OpenAPI must describe the HTTP surface actually implemented.

No ADR was created because this is a versioned external contract decision, not a selection of application architecture or infrastructure.

## Still unresolved

- HTTP method, route, success response, and validation-error representation.
- Whether the first endpoint accepts one event, a batch, or both.
- Field-length, payload-size, and nesting limits.
- Handling of unknown envelope properties.
- Persistence schema, data-access approach, migrations, and local PostgreSQL setup.
- Identifier and idempotency behavior.
- The request model, OpenAPI operation, and automated contract tests.

## Next recommended step

Define the smallest remaining HTTP behavior needed for the Stage 1 vertical slice—endpoint shape, single-versus-batch scope, and observable success and validation results—then implement it together with PostgreSQL persistence, matching OpenAPI, and an integration test.
