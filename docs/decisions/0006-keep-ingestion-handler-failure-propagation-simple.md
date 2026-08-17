# ADR 0006: Keep Ingestion Handler Failure Propagation Simple

**Date:** 2026-08-17

## Status

Accepted

## Context

PLAN 002 Step 4 introduces non-HTTP ingestion orchestration and accounting. Normal
completion needs to report how many records became durable and how many input records
were excluded before persistence. A persistence call can also fail after earlier
chunks have committed, leaving the overall use case unsuccessful even though some
records are durable.

The handler could represent that failure with a partial result, a custom exception
carrying accounting, or a hierarchy of success and failure outcomes. Those models
would anticipate an HTTP contract and retry behavior that have not yet been decided.

## Considered Options

1. Return a partial-failure result containing accepted, rejected, and not-committed
   accounting.
2. Wrap persistence failures in a custom ingestion exception carrying partial
   accounting.
3. Let the persistence exception propagate and return a result only after normal
   completion.

Options 1 and 2 retain more request-level accounting at the failure boundary, but add
models whose intended caller behavior is not yet known. Option 3 keeps the current use
case explicit and small, while requiring a later HTTP decision to account for the
possibility that earlier chunks committed before failure.

## Decision

On normal completion, `IngestEventsHandler` returns a small non-HTTP
`IngestEventsResult` containing only:

- `Accepted`: records whose chunk store call completed successfully;
- `Rejected`: input records excluded before persistence because they were malformed
  JSON or failed Event Contract v1 validation.

Malformed and contract-invalid records share the one rejected total. Separate
counters are not introduced yet.

If `IEventChunkStore.StoreAsync` throws, the handler does not convert the exception
into a result and does not wrap it in a custom ingestion exception. The persistence
exception propagates normally. Earlier successfully stored chunks remain durable, the
failing chunk is not counted as accepted, and no `IngestEventsResult` is returned
because the use case did not complete successfully.

This deliberately permits a caller to know that the overall request failed while
some earlier chunks are already committed. Cancellation follows the same normal .NET
propagation model and is not converted into a result.

This decision does not select HTTP status or response behavior.

## Consequences

The normal result and handler control flow remain small and contain no HTTP or
persistence-specific failure model. Rejected always means invalid input excluded
before persistence; it never includes a valid record from a failed store call.

On persistence failure, partial accepted/rejected counters are not available as a
handler result. Earlier commits are still observable in persistence, and retrying an
uncertain request can resend already durable records. The resulting retry and
idempotency problem is an accepted trade-off and remains unresolved until a later
stage.
