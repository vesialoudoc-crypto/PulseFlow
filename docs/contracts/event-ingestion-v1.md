# Event Ingestion Contract v1

**Status:** Accepted for implementation  
**Implemented:** No

## Purpose

This document defines the human-readable semantics of the first event envelope accepted for PulseFlow ingestion. It fixes the event fields and the boundary of `payload`; it does not claim that an ingestion endpoint currently exists.

When the endpoint is implemented, the ASP.NET Core OpenAPI document must describe the HTTP surface that the running API actually accepts. This document answers what PulseFlow promises clients; OpenAPI will answer what the current implementation exposes. A mismatch between them is a contract defect.

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

## Not defined by this decision

The following HTTP contract details remain unresolved and must not be inferred from this document:

- the HTTP method and endpoint path;
- whether v1 accepts one event, a batch, or both;
- success and error response bodies and status codes;
- identifier generation and idempotency behavior;
- maximum lengths, payload size, and nesting limits;
- treatment of unknown properties outside `payload`;
- authentication and authorization.

These details should be selected only as needed for the smallest verifiable Stage 1 vertical slice.
