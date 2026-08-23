# AGENTS.md

## Project

This repository contains a learning and portfolio project focused on
production-style backend engineering with ASP.NET Core.

Do not assume architecture or infrastructure decisions that have not been
explicitly documented.

## Required context

Before planning or implementing a task, read:

1. `docs/00_PROJECT_CONTEXT.md`
2. `docs/01_SOURCE_OF_TRUTH.md`
3. the most recent checkpoint in `docs/progress/`

Also read `docs/02_ROADMAP.md` when selecting or planning the next task.
Checkpoint filenames are ordered as `YYYY-MM-DD-NNN-slug.md`; the last filename in
lexicographic order is the most recent checkpoint. Read earlier checkpoints only
when historical context is needed.

## Document authority

Use the documents for different kinds of information:

- `00_PROJECT_CONTEXT.md` — project purpose, learning goals, and working principles.
- `01_SOURCE_OF_TRUTH.md` — stable product boundaries and mandatory properties.
- `02_ROADMAP.md` — planned sequence of implementation and learning stages.
- `docs/contracts/` — accepted external contracts and their human-readable semantics.
- `docs/progress/` — immutable chronological checkpoints describing the state reached
  at each meaningful milestone, verification results, unresolved questions, and the
  next recommended step.
- `docs/architecture/` — architecture that has actually been implemented or accepted.
- `docs/decisions/` — accepted architectural decisions and their consequences.

Do not interpret roadmap items as already accepted architecture.

## Working rules

- Prefer the smallest verifiable vertical slice.
- Do not add infrastructure or technologies only because they appear in the roadmap.
- Clearly distinguish existing facts, assumptions, proposals, and accepted decisions.
- Do not silently choose unresolved technologies or architecture.
- Discuss meaningful alternatives before making a decision with long-term consequences.
- Create an ADR when a significant architectural choice is accepted.
- Keep implementation and documentation consistent.
- Do not use C# primary constructors. Declare constructors explicitly inside the type body using traditional constructor syntax.

## Testing

- Every test uses explicit `// Arrange`, `// Act`, `// Assert`.
- Test names follow `Method_Scenario_ExpectedResult`.
- One test verifies one logical behavior or scenario.
- Avoid Eager Test: do not test several unrelated behaviors in one test.
- Avoid Assertion Roulette: assertions in one test must belong to the same logical result.
- Multiple assertions are allowed when they collectively verify one logical result.
- Do not split one logical result into several nearly identical tests merely to achieve one assertion per test.
- Prefer `[Theory]` when several inputs exercise the same behavior.
- Avoid duplicated Arrange/Act code across many nearly identical tests.
- Prefer readable, sufficient tests over exhaustive permutation or coverage-driven test suites.
- Test helpers should make tests easier to read, not hide test intent.
- Group non-test methods in test classes inside a `#region Test helpers` block to separate them from test methods.
- Do not refactor unrelated existing tests unless the task explicitly requires it.

## Documentation updates

- Write all project documentation in English.

After a meaningful implementation milestone:

- create a new checkpoint in `docs/progress/`;
- update `docs/02_ROADMAP.md` if stage status or expected result changed;
- update `docs/01_SOURCE_OF_TRUTH.md` only if product boundaries or mandatory properties changed;
- update architecture documentation when accepted architecture changes;
- create or update an ADR when a significant decision is made.

Every checkpoint must contain:

- the starting point;
- what changed;
- the resulting repository state;
- verification commands and results;
- decisions made, with ADR links where applicable;
- intentionally unresolved items;
- the next recommended step.

Existing checkpoints are historical records and must not be rewritten, except to
correct factual errors. Create a new checkpoint for later developments.

## Continuous integration

- GitHub Actions CI already exists in `.github/workflows/ci.yml`.
- Do not create, replace, redesign, or expand CI unless a task explicitly requires it.
- CI is additional verification after changes are pushed.
- CI does not replace local verification before considering implementation complete.
- Do not commit or push automatically unless explicitly requested.

## Verification

Before considering implementation work complete:

1. Run the standard repository checks below.
2. Check that the newest checkpoint and roadmap describe the resulting state.
3. Report unresolved issues or assumptions explicitly.

Run the project checks from the repository root:

```powershell
dotnet csharpier check .
dotnet build PulseFlow.slnx -warnaserror
pwsh ./scripts/test.ps1
pwsh ./scripts/check-project-docs.ps1
```
