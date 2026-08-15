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
3. `docs/04_CURRENT_STATE.md`

Also read `docs/02_ROADMAP.md` when selecting or planning the next task.

## Document authority

Use the documents for different kinds of information:

- `00_PROJECT_CONTEXT.md` — project purpose, learning goals, and working principles.
- `01_SOURCE_OF_TRUTH.md` — stable product boundaries and mandatory properties.
- `02_ROADMAP.md` — planned sequence of implementation and learning stages.
- `04_CURRENT_STATE.md` — what actually exists now, open questions, and the next step.
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

## Documentation updates

After a meaningful implementation change:

- update `docs/04_CURRENT_STATE.md`;
- update `docs/02_ROADMAP.md` if stage status or expected result changed;
- update `docs/01_SOURCE_OF_TRUTH.md` only if product boundaries or mandatory properties changed;
- update architecture documentation when accepted architecture changes;
- create or update an ADR when a significant decision is made.

Do not use `04_CURRENT_STATE.md` as a development diary.
Git history and ADRs preserve historical information.

## Verification

Before considering implementation work complete:

1. Build the solution.
2. Run relevant automated tests.
3. Check that documentation describes the resulting state.
4. Report unresolved issues or assumptions explicitly.
pwsh ./scripts/check-project-docs.ps1

Exact build and test commands should be added here once the solution structure exists.