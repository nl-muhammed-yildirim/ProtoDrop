# US-054-01 — Emit an event from a use case and have it reach the broker exactly once

**Feature:** F-FND-006 — Event Backbone (outbox → Service Bus) | **Status:** done (T-006/T-006b, 2026-09-03)

---

**Story:** As a developer writing a use case that changes state (e.g. `SendTransferCommand` later), I want to emit its domain event through one call that also records it in the outbox, so that business data and its events commit together — no "data saved but event lost" window.
**Actor:** Developer (human or AI session).
**Goal:** One transaction: business row + `DomainEvents` row; one publish; the broker sees exactly one message with the exact TA-5.2 envelope.

## Preconditions

- Event backbone in place (T-006/T-006b).
- A use case that writes business data and emits one event.

## Happy path

1. Use case calls `IEventPublisher.Publish(envelope)` inside its command handler.
2. The envelope is written as a `DomainEvents` row in the **same transaction** as the business data (EF Core change tracker).
3. After commit, the publisher sends it to Service Bus topic `core` with `MessageId = eventId`.
4. On success the outbox row is marked published (`IsPublished`).

## Alternative flows

- **Publish fails after commit:** the row stays unpublished; a re-publish picks it up (TA-4.1.7) — no silent drop, no duplicate business data.
- **Envelope shape drift:** unit tests assert the exact TA-5.2 fields for every event type in the closed TA-5.3 list — anything else fails CI.

## Acceptance criteria

```gherkin
Given a command that writes business data and emits one domain event
When it completes successfully
Then a DomainEvents row exists with the exact TA-5.2 envelope fields
And the event is published to the broker (or in-memory fake) once

Given any emitted event
When I inspect its serialized form
Then it contains exactly eventId, eventType, version, occurredAt, correlationId, partitionKey, payload
```

## Edge cases

- Dedup identity: `eventId` = Service Bus `MessageId` — the broker itself refuses duplicates (FR-054-4).
- MVP simplification per TA-4.1.7: at-most-once is accepted for non-critical events; the outbox row + `PUBLISH_FAILED` alert cover the rest.

## UI notes

- None.

## Technical notes

- Domain events stay MediatR-free (TA-0.2 rule 9): no `IPublisher.Publish(DomainEvent)` in `wa.application`.
- T-006b added the outbox write-on-change + handler processing; idempotency retry on failure is unit-tested.

## Links

- Feature: `F-FND-006-event-backbone.md` (FR-054-2, FR-054-3, AC-054-1/2)
- Architecture: TA-5.2, TA-4.1.7, TA-0.2(9)
- Milestone: T-006, T-006b
