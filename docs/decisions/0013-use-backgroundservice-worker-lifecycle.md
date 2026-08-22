# ADR 0013: Use the normal BackgroundService worker lifecycle

**Date:** 2026-08-22

## Status

Accepted

## Context

`EventParserConsumer` owns multiple competing RabbitMQ parser workers in one hosted
service. Its former lifecycle code created a linked cancellation token, waited for the
first worker to finish, cancelled sibling workers after a failure, and preserved an
initiating exception through sibling cleanup.

That coordination is not required by the delivery policy. It makes host shutdown and
worker-failure behavior more complex without adding a selected recovery or health
policy.

## Options

1. Let `BackgroundService` and the host control worker lifetime through the host
   stopping token and `Task.WhenAll`.
2. Retain custom worker supervision, sibling cancellation, and initiating-exception
   preservation.
3. Add a worker restart, health-check, or recovery policy.

## Decision

`EventParserConsumer` starts exactly `RabbitMq:ConsumerCount` workers. Each worker
receives the `BackgroundService` host stopping token, and `ExecuteAsync` awaits all
worker tasks with `Task.WhenAll`.

The consumer does not create a linked cancellation token, wait for any individual
worker, cancel sibling workers, restart workers, or coordinate worker failures. A
worker exception naturally faults the `BackgroundService` after `Task.WhenAll`
completes. Normal host shutdown is controlled only by the host stopping token.

This supersedes the worker-supervision portion of ADR 0010. It does not change
delivery processing, acknowledgement, rejection, dead-letter topology, persistence,
or idempotency semantics.

## Consequences

- A failed worker does not cause `EventParserConsumer` to cancel its siblings.
- There is no selected worker recovery or replacement policy.
- One delivery still receives one processing attempt: successful processing
  acknowledges it, processing failure terminally rejects it, and host cancellation
  leaves it unsettled.
- The hosted service finishes only after every worker task has finished; any worker
  exception is then surfaced by normal `BackgroundService` behavior.

## Explicit limitations

- This decision does not add worker health checks, automatic restart, or recovery.
- If a worker fails while another worker continues to run, the hosted service remains
  awaiting all worker tasks until normal host shutdown or their completion.
- RabbitMQ delivery guarantees, retry queues, redrive, Outbox, prefetch, and manual
  recovery remain unresolved.
