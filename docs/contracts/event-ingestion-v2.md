# Event Ingestion Contract v2

**Status:** Accepted and implemented
**Implementation:** `POST /api/events` accepts a raw `application/x-ndjson` batch for
asynchronous RabbitMQ processing. `EventParserConsumer` parses every record against
this contract. There is no concurrent v1 implementation, versioned route, or API
versioning framework: this is a coordinated breaking change for a project with no real
external v1 clients.

## Purpose

Event Contract v2 adds the stable client-provided identity needed to handle repeated
delivery of the same logical event. It retains the v1 envelope rules for `type`,
`source`, `occurredAt`, and the opaque object-valued `payload`.

## Event envelope

```json
{
  "eventId": "c7447aa0-37a2-4ea5-9c06-8e97a06e1a4d",
  "type": "string",
  "source": "string",
  "occurredAt": "2026-08-15T17:20:00Z",
  "payload": {}
}
```

All five properties are required.

## Field rules

| Property | JSON type | Rules |
|---|---|---|
| `eventId` | string | Required valid UUID. It is generated and owned by the event source. The source reuses it when resending the same logical event and generates a new one for a genuine new occurrence. |
| `type` | string | Required and non-empty. Identifies the kind of event as defined by the sender. |
| `source` | string | Required and non-empty. Identifies the event source as defined by the sender. |
| `occurredAt` | string | Required RFC 3339 timestamp in UTC with the uppercase `Z` UTC designator. |
| `payload` | object | Required arbitrary JSON object. An empty object is valid; arrays, scalars, and `null` are not valid top-level values. |

The logical event identity is the pair **`(source, eventId)`**. Different sources may
use the same UUID. `EventRecord.Id` remains a server-generated primary key for the
stored row; it is not the client event identity.

`payload` remains opaque: PulseFlow does not interpret its internal fields and stores
its JSON value rather than its original textual whitespace or property order.

## Record-level outcomes

Missing or malformed `eventId` is a normal contract-invalid record. It is not
persisted, and it does not cause the complete RabbitMQ batch to be dead-lettered.

A valid record whose `(source, eventId)` already exists is successfully handled as a
duplicate no-op. It does not create a second row, fail the batch, or send the batch to
the dead-letter queue.

## HTTP ingestion contract

- Method and route: `POST /api/events`.
- Request media type: `application/x-ndjson`.
- The API publishes the complete raw batch to RabbitMQ without parsing records.
- HTTP `202 Accepted` means RabbitMQ confirmed raw-batch publication. It does not
  report validation, duplicate, or persistence outcomes.
- Unsupported media type returns HTTP `415 Problem Details`; publisher and other
  unhandled failures return HTTP `500 Problem Details` without internal details.

## Limits of this contract

- It does not promise exactly-once RabbitMQ delivery or exactly-once processing.
- Clients and sources must retain and reuse `eventId` when resending the same event.
- Maximum sizes, unknown envelope properties, authentication, batch status, automatic
  dead-letter redrive, and retry policy remain outside this contract.
