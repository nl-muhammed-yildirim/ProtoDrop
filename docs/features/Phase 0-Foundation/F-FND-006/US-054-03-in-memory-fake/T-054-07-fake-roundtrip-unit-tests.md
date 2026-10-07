# T-054-07 — Unit tests: fake round-trip (publish → store → retrieve by topic)

**Story:** US-054-03 | **Spec:** AC-054-4, FR-054-5 + T-006 exit evidence (7/7) | **Size:** M
**Depends on:** T-054-05 (fake + DI wiring in place)

---

## Context to read (only these)

- `US-054-03-in-memory-fake.md` → Gherkin blocks + Technical notes (T-006 exit evidence line)
- `../../F-FND-006-event-backbone.md` → Test plan (unit lines only)

## Instructions

1. Add unit tests against the in-memory fake:
   - **publish → store → retrieve by topic**: publish to topic `core`, read back per topic — no cross-topic contamination
   - **TTL expiry**: expired entries leave the fake's store
   - round-trip fidelity: the envelope arrives with its exact TA-5.2 fields (second Gherkin block)
2. These are the T-006 exit-evidence tests (7/7 suite) — no live broker involved.

## Exit check

- [ ] Publish → store → retrieve by topic passes; no cross-topic contamination
- [ ] TTL expiry behaves per the fake's contract (fake clock, no real waits)
- [ ] Round-trip envelope is byte-exact TA-5.2 (AC-054-4 second block)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; in-memory fake publisher + DI wiring in place).
Task T-054-07 — unit-test the fake round-trip.
Read first (only): docs/features/Phase 0-Foundation/F-FND-006/US-054-03-in-memory-fake/US-054-03-in-memory-fake.md (Gherkin blocks + Technical notes T-006 exit evidence line) and F-FND-006-event-backbone.md (Test plan unit lines).
Do exactly:
1. Add unit tests against the in-memory fake publisher:
   - publish → store → retrieve by topic: publish to topic core, read back per topic — no cross-topic contamination;
   - TTL expiry: expired entries leave the fake's store (fake clock, no real waits);
   - round-trip fidelity: an event published to the fake arrives with its exact TA-5.2 fields when a consumer reads it (AC-054-4 second Gherkin block).
Done when: all three groups pass — these are the T-006 exit-evidence tests (the 7/7 suite), and no live broker is involved anywhere.
Constraints: fake only — no SB, no Testcontainers; assert exact envelope fields on read-back.
```
