# US-054-02 — Replay the same event without double-processing

**Feature:** F-FND-006 — Event Backbone (outbox → Service Bus) | **Status:** done (T-006, 2026-09-02)

---

**Story:** As a consumer developer (f-email, analytics receiver), I want the backbone to guarantee that replaying an already-seen event is a no-op, so that at-least-once delivery never turns into double emails or double metrics.
**Actor:** Developer writing consumers (T-019+), ops (replays after incidents).
**Goal:** Dedup by `eventId` — broker level (`MessageId`) plus consumer level (15-min LRU) — makes replay safe.

## Preconditions

- Event backbone in place (T-006).
- A consumer that processes events from topic `core`.

## Happy path

1. An event with `eventId X` is delivered and processed once; the consumer records X as seen.
2. The broker redelivers the same envelope (at-least-once semantics, or a manual replay).
3. The consumer's 15-min LRU recognizes X — processing is skipped, no side effect repeats.

## Alternative flows

- **Broker-level dedup:** publishing twice with the same `MessageId` to topic `core` results in one stored message (TA-5.1) — the first line of defense.
- **LRU expired but event re-delivered:** consumer must still be idempotent by design (e.g. email send checks state, not just memory) — the LRU is an optimization, not the guarantee.

## Acceptance criteria

```gherkin
Given a consumer that has already processed an event with eventId X
When the same envelope (eventId X) is delivered again within 15 minutes
Then no side effect repeats (dedup by eventId)

Given two publishes of the same envelope to topic core
When I inspect stored messages
Then only one message exists (MessageId = eventId dedup, TA-5.1)
```

## Edge cases

- Different eventIds, same partitionKey: order preserved per partition; consumers remain idempotent anyway (EC-054-2).
- Consumer crash mid-processing: redelivery hits the LRU/state check — no double side effect.

## UI notes

- None.

## Technical notes

- TA-5.1 topology: topic `core` Standard tier, 30-day retention, dedup by `MessageId`; subscriptions `email` (max delivery 5), `analytics` (3), `admin-alerts` (3).
- T-006 unit tests cover publish → store → retrieve by topic, TTL expiry, and no cross-topic contamination.

## Links

- Feature: `F-FND-006-event-backbone.md` (FR-054-4, AC-054-3)
- Architecture: TA-5.1, TA-5.2
- Milestone: T-006
