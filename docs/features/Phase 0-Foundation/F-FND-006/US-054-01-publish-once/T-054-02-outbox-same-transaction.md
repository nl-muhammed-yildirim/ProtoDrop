# T-054-02 — Outbox: DomainEvents row in the same transaction (T-006b)

**Story:** US-054-01 | **Spec:** FR-054-3, AC-054-1 (first block), EC-054-1 | **Size:** M
**Depends on:** T-052-03 (WaDbContext + migrations in place — the DomainEvents table needs its own migration), T-054-01 (envelope shape)

---

## Context to read (only these)

- `US-054-01-publish-once.md` → happy path + Alternative flows
- `../../F-FND-006-event-backbone.md` → FR-054-3/6 + EC-054-1 + Technical notes (outbox line)

## Instructions

1. Add the **`DomainEvents`** table via a new EF migration (it is not one of TA-3.2's 15 tables — golden rule 4: new table = new migration + ADR note in TA-17).
2. The publisher writes the envelope as an outbox row **in the same transaction** as the business data via the EF Core change tracker (T-006b) — no distributed commit, no manual SQL.
3. On publish success mark the row `IsPublished`; on failure it stays for re-publish (EC-054-1 — no silent drop).

## Exit check

- [ ] A command that writes business data + emits one event commits both atomically (kill mid-transaction → neither exists)
- [ ] After a successful publish the outbox row is `IsPublished = true`; after a failed publish it stays `false` and re-publish picks it up

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; WaDbContext + migrations in place; EventEnvelope + IEventPublisher port defined).
Task T-054-02 — implement the outbox write.
Read first (only): docs/features/Phase 0-Foundation/F-FND-006/US-054-01-publish-once/US-054-01-publish-once.md (happy path + Alternative flows) and F-FND-006-event-backbone.md (FR-054-3/6 + EC-054-1).
Do exactly:
1. Add the DomainEvents table via a new EF migration — it is not one of TA-3.2's 15 tables, so golden rule 4 applies: new migration + ADR note in TA-17.
2. Have the publisher write the envelope as an outbox row in the same transaction as the business data using the EF Core change tracker (T-006b) — no distributed commit, no manual SQL.
3. On publish success mark the row IsPublished = true; on failure leave it false so a re-publish picks it up (EC-054-1 — no silent drop).
Done when: business data and its event commit atomically in one transaction, and the outbox row state transitions correctly (unpublished → published) including the failure path.
Constraints: publish timeout 10 s (TA-4.1.7); MVP simplification at-most-once for non-critical events is documented per TA-4.1.7 — do not add retry infrastructure beyond re-publish of unpublished rows.
```
