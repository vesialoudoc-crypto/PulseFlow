# Event Ingestion Contract v1

**Status:** Accepted and implemented
**Implementation:** The accepted Event Contract v1 envelope and its reusable parsing,
validation, and persistence components are implemented. `POST /api/events` accepts
the complete raw NDJSON batch for asynchronous RabbitMQ processing; record-level
outcomes are not available in the HTTP response.

## Purpose

This document defines the human-readable semantics of the first event envelope accepted
for PulseFlow ingestion. It fixes the event fields and the boundary of `payload`, and
documents the implemented asynchronous HTTP acceptance boundary.

The ASP.NET Core OpenAPI document describes the HTTP surface that the running API
actually accepts. This document answers what PulseFlow promises clients; OpenAPI
answers what the current implementation exposes. A mismatch between them is a
contract defect.

## Event envelope

A minimal valid event has this JSON shape:

```json
{
  "type": "string",
  "source": "string",
  "occurredAt": "2026-08-15T17:20:00Z",
  "payload": {}
}
```

All four properties are required.

## Field rules

| Property | JSON type | Rules |
|---|---|---|
| `type` | string | Required and non-empty. Identifies the kind of event as defined by the sender. |
| `source` | string | Required and non-empty. Identifies the event source as defined by the sender. |
| `occurredAt` | string | Required RFC 3339 timestamp in UTC. The serialized value uses the UTC designator `Z`. |
| `payload` | object | Required arbitrary JSON object. An empty object is valid. Arrays, strings, numbers, booleans, and `null` are not valid top-level payload values. |

## Payload semantics

- PulseFlow does not know or validate the internal structure of `payload`.
- The event sender defines the payload fields and their meaning.
- Payload properties may have any names, JSON value types, and nesting depth within limits that will be defined separately.
- The server must persist the payload as one JSON value without mapping its contents to sender-specific domain fields.
- The contract preserves the JSON data, not its original textual representation. It does not promise byte-for-byte preservation of whitespace or property order.
- Sender-specific models such as `PaymentPayload` or `PlayerPayload` are outside the PulseFlow ingestion contract.

For OpenAPI, `payload` must be represented as an object that permits arbitrary additional properties, not as an unconstrained value that also accepts arrays or scalars.

## Examples

A payload with arbitrary nested sender data is valid:

```json
{
  "type": "payment.completed",
  "source": "billing-service",
  "occurredAt": "2026-08-15T17:20:00Z",
  "payload": {
    "paymentId": "pay-123",
    "amount": {
      "value": 42.50,
      "currency": "USD"
    },
    "tags": ["portfolio", "demo"]
  }
}
```

The following top-level payload values are invalid:

```json
{ "payload": [1, 2, 3] }
```

```json
{ "payload": 42 }
```

```json
{ "payload": null }
```

These fragments illustrate only the invalid `payload` shape; they are not complete requests.

## HTTP ingestion contract

- Method and route: `POST /api/events`.
- Request media type: `application/x-ndjson`.
- The complete raw NDJSON request body is published to RabbitMQ without individual
  record parsing or Event Contract v1 validation in the API request path.
- Successful RabbitMQ publication confirmation returns HTTP 202 with no response body.
  It means only that RabbitMQ accepted the raw batch; it does not report record totals,
  validation results, or persistence results.
- A malformed or contract-invalid individual record can receive HTTP 202 when the
  publisher accepts the raw batch. Its later processing outcome is not yet exposed by
  a public contract.
- An unsupported request media type returns HTTP 415 Problem Details.
- Publisher and other unhandled failures return HTTP 500 Problem Details without
  exception messages, stack traces, SQL details, or record-level accounting.

The asynchronous acceptance boundary is recorded in
[ADR 0009](../decisions/0009-define-stage-2-rabbitmq-batch-acceptance-boundary.md).

## Not defined by this decision

The following contract details remain unresolved and must not be inferred from this document:

- identifier generation and idempotency behavior;
- maximum lengths, payload size, and nesting limits;
- treatment of unknown properties outside `payload`;
- authentication and authorization.

The `EventParserConsumer` and the public representation of later parsing,
validation, persistence, or batch-status outcomes are also not implemented.

These details should be selected only as needed for the smallest verifiable Stage 1 vertical slice.
