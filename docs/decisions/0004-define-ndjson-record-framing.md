# ADR 0004: Define NDJSON Record Framing

**Date:** 2026-08-17

## Status

Accepted

## Context

[ADR 0001](0001-use-ndjson-for-batch-ingestion.md) selected NDJSON and independent
record handling but left the exact stream framing edge cases unresolved. The
asynchronous reader in PLAN 002 Step 2 needs deterministic boundaries for newline
variants, blank records, clean end-of-stream, malformed final data, numbering, and
cancellation.

## Considered options

Require every record to end with a newline, or accept a syntactically complete final
JSON value when the stream ends cleanly. Requiring a newline is stricter framing;
accepting a complete final value tolerates a common omission while retaining exact
JSON syntax validation.

## Decision

1. LF (`\n`) terminates an NDJSON record.
2. CRLF (`\r\n`) also terminates an NDJSON record.
3. A blank line is an empty record and produces a malformed-record result; it is not
   ignored.
4. A syntactically complete final JSON value at clean end-of-stream is a complete
   record even without a trailing newline.
5. Incomplete or malformed final JSON produces a malformed-record result. The reader
   never attempts heuristic repair.
6. Record numbering is one-based and follows physical stream order.
7. Cancellation is not malformed input. It propagates through normal .NET
   cancellation semantics, such as `OperationCanceledException`.
8. Step 2 distinguishes only successfully parsed JSON from malformed JSON. It does
   not introduce a detailed JSON parser error-code taxonomy.

## Consequences

Accepting a complete final JSON value without a trailing newline is more tolerant
while preserving explicit JSON syntax validation. Malformed or incomplete final data
is never repaired. The reader can continue after a malformed newline-terminated
record because the next LF-defined boundary remains explicit.

This decision does not determine unknown top-level Event Contract properties,
record or upload size limits, HTTP error shape, or persistence behavior.
