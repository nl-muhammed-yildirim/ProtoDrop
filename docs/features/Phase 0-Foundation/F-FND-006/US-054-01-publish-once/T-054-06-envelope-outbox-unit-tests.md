# T-054-06 — Unit tests: envelope shape per TA-5.3 type + outbox retry idempotency

**Story:** US-054-01 | **Spec:** AC-054-2 (unit half), FR-054-3, EC-054-1 | **Size:** M
**Depends on:** T-054-03 (adapter + failure path implemented)

---

## Context to read (only these)

- `US-054-01-publish-once.md` → Alternative flows (envelope shape drift line)
- `../../F-FND-006-event-backbone.md` → Test plan (unit lines only) + EC-054-1

## Instructions

1. Add unit tests to the relevant test project:
   - **Envelope shape per event type** for every type in the closed TA-5.3 list — exactly the seven fields, nothing else (AC-054-2; "anything else fails CI")
   - **Outbox write-on-change + idempotent retry** (T-006b): a failed publish leaves the row unpublished; re-publish of the same envelope does not create a second business effect or a duplicate message

## Exit check

- [ ] Every TA-5.3 event type's serialized envelope matches the exact seven-field shape
- [ ] Idempotent retry: publishing the same eventId twice produces one stored message (MessageId dedup) and no double side effect

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; outbox + SB adapter implemented).
Task T-054-06 — unit-test the envelope contract and retry idempotency.
Read first (only): docs/features/Phase 0-Foundation/F-FND-006/US-054-01-publish-once/US-054-01-publish-once.md (Alternative flows) and F-FND-006-event-backbone.md (Test plan unit lines + EC-054-1).
Do exactly:
1. Add unit tests asserting the envelope shape for every event type in the closed TA-5.3 list — exactly eventId, eventType, version, occurredAt, correlationId, partitionKey, payload and nothing else (AC-054-2; anything else fails CI).
2. Add outbox write-on-change + idempotent retry tests (T-006b): a failed publish leaves the row unpublished, re-publish of the same envelope creates no second business effect and no duplicate message (MessageId dedup).
Done when: both test groups pass — the closed TA-5.3 list is pinned field-by-field, and retry is provably idempotent.
Constraints: fakes only (no live broker); assert exact serialized shape — not just "has fields".
```
