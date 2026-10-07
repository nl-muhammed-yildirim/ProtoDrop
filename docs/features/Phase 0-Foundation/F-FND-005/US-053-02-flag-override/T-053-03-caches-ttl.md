# T-053-03 — PlansCache + FlagsCache with 30 s TTL and injectable clock

**Story:** US-053-02 | **Spec:** FR-053-4, AC-053-3 | **Size:** M
**Depends on:** T-053-02 (stores to cache over)

---

## Context to read (only these)

- `US-053-02-flag-override.md` → second Gherkin block + Technical notes
- `../../F-FND-005-limits-registry.md` → FR-053-4 + AC-053-3

## Instructions

1. Add **`PlansCache`** and **`FlagsCache`** to `wa.application/Limits/`:
   - 30 s TTL — the value comes from `Wa:Flags:TtlSeconds` (TA-3.4 technical constant), not a literal in code
   - injectable clock (`Func<DateTimeOffset>`) so tests don't wait real seconds
2. **Every read goes through the caches** — no direct store calls outside them.

## Exit check

- [ ] A cached entry younger than 30 s is served from cache (no refetch)
- [ ] After 30 s the next read refetches (verified with the fake clock, not real waits)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; IPlansStore/IFlagsStore ports + adapters in place).
Task T-053-03 — add the TTL caches.
Read first (only): docs/features/Phase 0-Foundation/F-FND-005/US-053-02-flag-override/US-053-02-flag-override.md (second Gherkin block + Technical notes) and F-FND-005-limits-registry.md (FR-053-4 + AC-053-3).
Do exactly:
1. Add PlansCache and FlagsCache to src/wa.application/Limits with a 30 s TTL read from Wa:Flags:TtlSeconds (TA-3.4 technical constant — no literal in code) and an injectable Func<DateTimeOffset> clock for tests.
2. Route every plan/flag read through the caches — no direct store calls outside them.
Done when: AC-053-3 holds — a cached entry younger than 30 s is not refetched, and after 30 s the next read refetches (proven with the fake clock).
Constraints: TTL value comes from configuration only; both caches share the same TTL constant.
```
