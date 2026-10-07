# T-054-05 — In-memory publisher fake + DI wiring (unset SB → fake)

**Story:** US-054-03 | **Spec:** FR-054-5, AC-054-4 | **Size:** M
**Depends on:** T-054-01 (port), T-054-03 (real adapter — the fake mirrors its contract)

---

## Context to read (only these)

- `US-054-03-in-memory-fake.md` → happy path + Gherkin blocks + Edge cases
- `../../F-FND-006-event-backbone.md` → FR-054-5 + AC-054-4 + Technical notes (adapter line)

## Instructions

1. Add the **in-memory fake publisher** implementing the same `IEventPublisher` contract: records envelopes with the same TA-5.2 shape and the same dedup-by-eventId behavior; supports reading back per topic so tests can do publish → consume round-trips.
2. Wire DI in `wa.api/Program.cs`: **no Service Bus connection string set → fake**; string set → SB adapter (TA-0.2 rule 7 — production code never branches on "is this the fake?").
3. No new packages: serialization uses existing `System.Text.Json` only.

## Exit check

- [ ] With no SB connection string, publishing through `IEventPublisher` records the event in the fake and a test consumer reads it back with exact TA-5.2 fields (round-trip)
- [ ] No "is this the fake?" branches anywhere — DI picks the adapter

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; IEventPublisher port + SB adapter in place).
Task T-054-05 — add the in-memory fake and the DI switch.
Read first (only): docs/features/Phase 0-Foundation/F-FND-006/US-054-03-in-memory-fake/US-054-03-in-memory-fake.md (happy path + Gherkin blocks + Edge cases) and F-FND-006-event-backbone.md (FR-054-5 + AC-054-4).
Do exactly:
1. Add the in-memory fake publisher implementing the same IEventPublisher contract — records envelopes with the exact TA-5.2 shape and dedup-by-eventId behavior, and supports reading back per topic so tests can do publish → consume round-trips.
2. Wire DI in src/wa.api/Program.cs: no Service Bus connection string set → fake; string set → SB adapter (TA-0.2 rule 7 — production code never branches on "is this the fake?").
3. Use existing System.Text.Json only — no new packages.
Done when: AC-054-4 holds — with no SB connection string, publishing through IEventPublisher records the event in the fake and a test consumer reads it back with exact TA-5.2 fields.
Constraints: the fake is a test double, not a second implementation — same contract, same dedup semantics; keep it out of wa.domain (it lives where tests can reach it).
```
