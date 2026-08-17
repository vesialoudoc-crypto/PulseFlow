# ADR 0005: Generate Event Persistence Metadata in the Application

**Date:** 2026-08-17

## Status

Accepted

## Context

Persisting a validated `EventEnvelope` requires an `EventRecord.Id` and
`EventRecord.ReceivedAt`, but neither value belongs to Event Contract v1. PLAN 002
Step 3 needs the smallest explicit assignment policy without adding abstractions for
requirements that do not yet exist.

## Considered options

1. Generate both values directly while translating an envelope to a persistence
   record.
2. Introduce an ID generator, clock, `TimeProvider`, factory, or mapper to control the
   values indirectly.

## Decision

`EventRecord.Id` and `EventRecord.ReceivedAt` are persistence-only metadata generated
by the application. When a validated `EventEnvelope` is translated into an
`EventRecord` for persistence:

- `Id` is assigned with `Guid.NewGuid()`;
- `ReceivedAt` is assigned with `DateTime.UtcNow`.

No `IIdGenerator`, `IClock`, `TimeProvider`, factory, mapper, or other abstraction is
introduced for these assignments. `EventEnvelope` remains independent of the
persistence-only `Id` and `ReceivedAt` values.

## Consequences

The mapping remains direct and readable. Tests observe generated IDs and receipt
times rather than controlling them deterministically. If deterministic ID or time
control becomes a real requirement later, the implementation can be refactored then.
