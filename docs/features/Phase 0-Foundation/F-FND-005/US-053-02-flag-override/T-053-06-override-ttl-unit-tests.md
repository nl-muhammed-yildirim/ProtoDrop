# T-053-06 — Unit tests: override wins for exactly one field + TTL honored

**Story:** US-053-02 | **Spec:** AC-053-2/3 (unit half), FR-053-4 | **Size:** M
**Depends on:** T-053-05 (test harness/fakes established)

---

## Context to read (only these)

- `US-053-02-flag-override.md` → both Gherkin blocks + Edge cases
- `../../F-FND-005-limits-registry.md` → AC-053-2/3 + Test plan (unit lines only)

## Instructions

1. Add unit tests to `wa.application.unit` using faked stores + fake clock:
   - **Override wins, precisely:** a flag setting `limits.free.maxTransferSize` to 10 GB changes only that one field on free — every other field of free and both other plans are unchanged (AC-053-2)
   - **TTL honored on both caches:** a cached entry younger than 30 s is not refetched; after 30 s the next read refetches (AC-053-3, fake clock — no real waits)
   - `feature.*` flags and unknown key segments are ignored by limit resolution (EC-053-1)

## Exit check

- [ ] Override test proves exactly-one-field semantics (AC-053-2)
- [ ] TTL tests pass for both PlansCache and FlagsCache via the fake clock (AC-053-3)
- [ ] Full T-005 suite is 9/9 green in wa.application.unit

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; LimitsProvider + caches implemented; resolution unit tests already present).
Task T-053-06 — unit-test override layering and TTL behavior.
Read first (only): docs/features/Phase 0-Foundation/F-FND-005/US-053-02-flag-override/US-053-02-flag-override.md (both Gherkin blocks + Edge cases) and F-FND-005-limits-registry.md (AC-053-2/3 + Test plan unit lines).
Do exactly:
1. Add unit tests to src/wa.application.unit using faked stores plus a fake clock:
   - override wins precisely: a flag setting limits.free.maxTransferSize to 10 GB changes only that one field on free — every other field of free and both other plans are unchanged (AC-053-2);
   - TTL honored on both caches: a cached entry younger than 30 s is not refetched, and after 30 s the next read refetches (AC-053-3) — use the fake clock, no real waits;
   - feature.* flags and unknown key segments are ignored by limit resolution (EC-053-1).
Done when: AC-053-2 and AC-053-3 hold in unit form and the full T-005 suite is 9/9 green.
Constraints: fakes + fake clock only — no Testcontainers; the override must be asserted field-by-field, not by reference equality.
```
