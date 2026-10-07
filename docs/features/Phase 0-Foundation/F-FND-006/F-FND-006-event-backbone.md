# F-FND-006 — Event Backbone (outbox → Service Bus)

**Priority:** P0 (foundation) | **Phase:** 0 — Foundation (M0)
**Spec source:** `Milestone-Backlog.md` T-006, TA-5.1/TA-5.2 | **Architecture:** TA-5.1, TA-5.2, TA-4.1.7, TA-0.2(9)
**Milestone tasks:** T-006, T-006b

---

## Description

Every feature after M0 talks to the outside world through events — `transfer.created` drives email and analytics, `email.failed` drives alerts, `download.completed` drives metrics. This feature builds that backbone once: domain events stay **MediatR-free** (TA-0.2 rule 9), use cases call an `IEventPublisher`, which writes the TA-5.2 envelope to an outbox row in the same transaction as the business data and then publishes it to Service Bus topic `core`. An in-memory fake stands in for the broker locally and in unit tests, so M0 never needs a live SB connection. Dedup by `eventId` (= Service Bus `MessageId`) is the contract every consumer inherits: replay of the same event must be a no-op.

**Actors:** developer/AI session (emits events from use cases), consumers (`f-email`, analytics receiver — arrive with T-019+), ops (watches DLQ/alerts).
**Value:** "publish once, consume anywhere" is decided at M0; T-011 onward never invents an event shape or a delivery guarantee.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-054-1 | Domain events are MediatR-free POCOs (TA-0.2 rule 9): use cases call `IEventPublisher.Publish(envelope)` instead of `IPublisher.Publish(DomainEvent)`, so `wa.application` never depends on MediatR's eventing. |
| FR-054-2 | Every message uses the TA-5.2 envelope exactly: `eventId` (GUID, = Service Bus `MessageId`), `eventType` (closed list from TA-5.3), `version` (1), `occurredAt` (UTC), `correlationId`, `partitionKey`, `payload`. No other fields; no ad-hoc event types in MVP. |
| FR-054-3 | Outbox pattern: the publisher writes the envelope as a row to the `DomainEvents` table **in the same transaction** as the business data (EF Core change tracker, T-006b), then publishes; on success the row is marked published (`IsPublished`), on failure it stays for re-publish. |
| FR-054-4 | Dedup by `eventId`: the broker dedups by `MessageId`, and consumers must be idempotent (15-min LRU against eventId). Replaying an already-seen event produces no side effects. |
| FR-054-5 | Locally and in tests, an in-memory fake publisher replaces Service Bus when the connection string is unset (F-FND-002 §5.2 rule) — same `IEventPublisher` contract, no new packages beyond existing `System.Text.Json` serialization. |
| FR-054-6 | Publish timeout 10 s (TA-4.1.7); on failure the outbox row remains + `PUBLISH_FAILED` alert path — MVP simplification: at-most-once accepted for non-critical events, documented per TA-4.1.7. |

## Acceptance criteria

```gherkin
AC-054-1: A use case publishes an event through the outbox
  Given a command that writes business data and emits one domain event
  When it completes successfully
  Then a DomainEvents row exists with the exact TA-5.2 envelope fields
  And the event is published to the broker (or in-memory fake) once

AC-054-2: The envelope is verbatim TA-5.2
  Given any emitted event
  When I inspect its serialized form
  Then it contains exactly eventId, eventType, version, occurredAt, correlationId, partitionKey, payload
  And eventId equals the Service Bus MessageId

AC-054-3: Replay does not double-process
  Given a consumer that has already processed an event with eventId X
  When the same envelope (eventId X) is delivered again
  Then no side effect repeats (dedup by eventId)

AC-054-4: No broker needed locally
  Given no Service Bus connection string is set
  When I publish through IEventPublisher in a unit or integration test
  Then the in-memory fake records the event and tests pass without SB
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-054-1 | Publish fails after the outbox row was written | Row stays unpublished; re-publish on next run — no silent drop (TA-4.1.7) |
| EC-054-2 | Two events with different eventIds but same partitionKey | Order preserved per partition (SB semantics); consumers still must be idempotent |
| EC-054-3 | A use case forgets to emit its event | Not caught at M0 — the closed TA-5.3 list + review keep it honest; T-011+ adds per-event tests |

## UI notes

- None (backend only).

## Technical notes

- `IEventPublisher` in `wa.application`; SB adapter in `wa.infrastructure`; in-memory fake for local/tests (F-FND-002 optional-SB rule).
- Outbox row lives with the EF change tracker (T-006b): one transaction, no distributed commit.
- T-006 exit evidence: unit tests 7/7 — publish → store → retrieve by topic, TTL expiry, no cross-topic contamination; full §4 gate green.

## Test plan

- Unit: envelope shape per event type (TA-5.3 closed list); dedup on replay; outbox write-on-change + idempotent retry (T-006b).
- Integration: publish → consume round-trip against the fake; `DomainEvents` row state transitions (unpublished → published).
- Gate: full AGENT.md §4 gate green.

## User stories & implementation tasks

| ID | Story / Task | File |
|---|---|---|
| US-054-01 | Emit an event from a use case and have it reach the broker exactly once | `US-054-01-publish-once/US-054-01-publish-once.md` |
| US-054-02 | Replay the same event without double-processing | `US-054-02-dedup-replay/US-054-02-dedup-replay.md` |
| US-054-03 | Test events end-to-end without a Service Bus connection | `US-054-03-in-memory-fake/US-054-03-in-memory-fake.md` |

**Implementation tasks:** one file per task — each story folder holds its story .md + its task files (context-friendly; execute top-to-bottom).

| Story | Task | File | Status |
|---|---|---|---|
| US-054-01 | T-054-01 EventEnvelope (TA-5.2 exact) + IEventPublisher port | `US-054-01-publish-once/T-054-01-event-envelope-publisher-port.md` | ☐ |
| US-054-01 | T-054-02 Outbox: DomainEvents row in the same transaction (T-006b) | `US-054-01-publish-once/T-054-02-outbox-same-transaction.md` | ☐ |
| US-054-01 | T-054-03 Service Bus adapter: publish to topic core (MessageId = eventId) | `US-054-01-publish-once/T-054-03-sb-adapter.md` | ☐ |
| US-054-02 | T-054-04 Consumer-side dedup: 15-min LRU by eventId (replay = no-op) | `US-054-02-dedup-replay/T-054-04-consumer-dedup-lru.md` | ☐ |
| US-054-03 | T-054-05 In-memory publisher fake + DI wiring (unset SB → fake) | `US-054-03-in-memory-fake/T-054-05-in-memory-fake.md` | ☐ |
| US-054-01 | T-054-06 Unit tests: envelope shape per TA-5.3 type + outbox retry idempotency | `US-054-01-publish-once/T-054-06-envelope-outbox-unit-tests.md` | ☐ |
| US-054-03 | T-054-07 Unit tests: fake round-trip (publish → store → retrieve by topic) | `US-054-03-in-memory-fake/T-054-07-fake-roundtrip-unit-tests.md` | ☐ |

**Story done when:** all tasks checked + full AGENT.md §4 gate green + the story's ACs verified. Then T-006 and T-006b can be marked `done` in `Milestone-Backlog.md`.
