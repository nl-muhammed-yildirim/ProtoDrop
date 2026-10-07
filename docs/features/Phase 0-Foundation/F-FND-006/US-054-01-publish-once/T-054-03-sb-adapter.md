# T-054-03 — Service Bus adapter: publish to topic core (MessageId = eventId)

**Story:** US-054-01 | **Spec:** FR-054-4/6, AC-054-1 (publish half), EC-054-1 | **Size:** M
**Depends on:** T-054-02 (outbox rows exist to publish)

---

## Context to read (only these)

- `US-054-01-publish-once.md` → happy path + Edge cases
- `../../F-FND-006-event-backbone.md` → FR-054-4/6 + EC-054-1 + Technical notes (adapter line)

## Instructions

1. Add the **Service Bus adapter** in `wa.infrastructure`: publishes envelopes to topic **`core`** with **`MessageId = eventId`** (broker-level dedup — FR-054-4).
2. Publish timeout **10 s** (TA-4.1.7); on failure the outbox row stays unpublished + `PUBLISH_FAILED` alert path (EC-054-1) — no silent drop.
3. MVP simplification documented per TA-4.1.7: at-most-once accepted for non-critical events; the outbox row + alert cover the rest.

## Exit check

- [ ] A published event arrives on topic `core` with MessageId equal to its eventId (verified against a real or fake broker)
- [ ] Failure path: timeout/failure leaves the row unpublished and raises the PUBLISH_FAILED alert path

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; outbox rows written in-transaction per T-054-02).
Task T-054-03 — add the Service Bus publishing adapter.
Read first (only): docs/features/Phase 0-Foundation/F-FND-006/US-054-01-publish-once/US-054-01-publish-once.md (happy path + Edge cases) and F-FND-006-event-backbone.md (FR-054-4/6 + EC-054-1).
Do exactly:
1. Add the Service Bus adapter in src/wa.infrastructure that publishes envelopes to topic core with MessageId = eventId (broker-level dedup, FR-054-4).
2. Use a 10 s publish timeout (TA-4.1.7); on failure leave the outbox row unpublished and raise the PUBLISH_FAILED alert path — no silent drop (EC-054-1).
3. Document the MVP simplification per TA-4.1.7: at-most-once accepted for non-critical events; the outbox row + alert cover the rest.
Done when: a published event arrives on topic core with MessageId equal to its eventId, and the failure path leaves the row unpublished with the PUBLISH_FAILED alert raised.
Constraints: SB topology per TA-5.1 (topic core, Standard tier, 30-day retention) — do not create topics/subscriptions in code at M0; DI picks this adapter only when a connection string is set (T-054-05 owns the wiring).
```
