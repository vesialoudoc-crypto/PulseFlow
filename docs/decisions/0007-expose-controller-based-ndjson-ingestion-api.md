# ADR 0007: Expose a Controller-Based NDJSON Ingestion API

**Date:** 2026-08-17

## Status

Accepted

## Context

PLAN 002 Step 5 must expose the implemented streaming reader, ingestion handler, and
chunk store through a public HTTP boundary. The contract needs predictable behavior
for mixed record validity, unsupported media types, and persistence failures without
moving parsing, validation, chunking, or exception interpretation into the endpoint.

## Considered Options

1. Expose the endpoint with ASP.NET Core Minimal APIs.
2. Expose the endpoint with an ASP.NET Core controller and centralize unhandled-error
   handling in middleware.

Both can provide a thin endpoint. Controllers are accepted for this public boundary
to make media-type and response metadata explicit through attributes while retaining
the existing orchestration types unchanged.

## Decision

PulseFlow exposes `POST /api/events` through an ASP.NET Core controller and consumes
`application/x-ndjson`. The controller passes `Request.Body` through
`NdjsonRecordReader` to `IngestEventsHandler` with request cancellation and returns
the result without parsing, validation, chunking, persistence logic, or exception
handling of its own.

Normal completion returns HTTP 200 with:

```json
{
  "total": 0,
  "accepted": 0,
  "rejected": 0
}
```

Malformed or contract-invalid individual records contribute to `rejected` without
failing the request. A normal result with zero accepted records and one or more
rejected records is still HTTP 200. Empty input returns HTTP 200 with zero for all
three fields. Unsupported media types return HTTP 415.

An unhandled or persistence failure becomes HTTP 500 Problem Details through the
global ASP.NET Core exception handler. The controller does not catch persistence
exceptions. The response does not expose exception messages, stack traces, SQL
details, or partial accounting. Earlier committed chunks may remain durable after a
later failure even though the overall response is 500.

The application uses `AddProblemDetails`, global exception handling, and status-code
pages for otherwise body-less error statuses. First-party ASP.NET Core OpenAPI
generation documents the endpoint. Swagger UI points to that document in Development
only. Application startup does not run EF Core migrations automatically.

## Consequences

The HTTP boundary remains thin and the existing handler stays independent of ASP.NET
Core. Normal partial-invalid input has a stable 200 response, while unexpected
failures have a safe centralized representation and deliberately no partial counts.
Clients can still face an uncertain retry outcome after a 500 because earlier chunks
may have committed; idempotency and deduplication remain deferred.

The controller choice does not introduce a service layer, mediator, command bus, or
custom error framework. Authentication, limits, resilience policies, health checks,
and other production hardening remain future decisions.
