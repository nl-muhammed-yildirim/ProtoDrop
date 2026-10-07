# T-054-04 — Consumer-side dedup: 15-min LRU by eventId (replay = no-op)

**Story:** US-054-02 | **Spec:** FR-054-4, AC-054-3 | **Size:** M
**Depends on:** T-054-01 (envelope shape — the dedup key is `eventId`)

---

## Context to read (only these)

- `US-054-02-dedup-replay.md` → happy path + Alternative flows + Gherkin blocks
- `../../F-FND-006-event-backbone.md` → FR-054-4 + AC-054-3 + EC-054-2

## Instructions

1. Add the consumer-side dedup mechanism: a **15-min LRU against eventId** that consumers use to skip already-seen events (the broker's `MessageId` dedup is the first line; this is the second).
2. The LRU is an **optimization, not the guarantee** — document that consumers must also be idempotent by design (e.g. email send checks state), because after 15 min the entry expires but redelivery can still happen.

## Exit check

- [ ] A consumer that already processed eventId X skips a redelivered envelope with X within 15 minutes — no side effect repeats (AC-054-3)
- [ ] After LRU expiry, re-delivery is handled by the consumer's own idempotency (documented + unit-tested)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; EventEnvelope + IEventPublisher in place).
Task T-054-04 — add the consumer-side dedup contract.
Read first (only): docs/features/Phase 0-Foundation/F-FND-006/US-054-02-dedup-replay/US-054-02-dedup-replay.md (happy path + Alternative flows + Gherkin blocks) and F-FND-006-event-backbone.md (FR-054-4 + AC-054-3).
Do exactly:
1. Add the consumer-side dedup mechanism — a 15-min LRU against eventId that consumers use to skip already-seen events (the broker's MessageId dedup is the first line; this is the second).
2. Document that the LRU is an optimization, not the guarantee: after expiry, redelivery must be handled by the consumer's own idempotency by design (e.g. email send checks state, not just memory).
Done when: AC-054-3 holds — a replay of eventId X within 15 minutes produces no repeated side effect, and the post-expiry path is covered by the consumer's idempotency (unit-tested).
Constraints: injectable clock for the 15-min window (no real waits in tests); different eventIds with the same partitionKey keep per-partition order (EC-054-2) — consumers remain idempotent anyway.
```
