# T-053-05 — Unit tests: per-plan seed resolution, guest → Free, unknown plan

**Story:** US-053-01 | **Spec:** AC-053-1/4 (unit half), FR-053-6 | **Size:** M
**Depends on:** T-053-04 (LimitsProvider implemented)

---

## Context to read (only these)

- `US-053-01-resolve-limits.md` → Gherkin blocks + Alternative flows
- `../../F-FND-005-limits-registry.md` → AC-053-1/4 + Test plan (unit lines only)

## Instructions

1. Add unit tests to `wa.application.unit` using **faked `IPlansStore`/`IFlagsStore` + fake clock** — no Testcontainers needed for resolution logic:
   - per-plan seed resolution: free/pro/business each return exactly their Part-2 constant values (AC-053-1)
   - guest requests resolve to Free's limits without an account lookup (FR-053-6)
   - unknown plan → typed failure mapping to 404 `PLAN_UNKNOWN` (AC-053-4)
   - DB down on cache miss → typed failure, no silent fallback (EC-053-3)

## Exit check

- [ ] All resolution unit tests pass in wa.application.unit (part of the T-005 9/9 suite — the override/TTL half lands in T-053-06)
- [ ] No Testcontainers used: fakes + fake clock only

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; LimitsProvider implemented with caches).
Task T-053-05 — unit-test the resolution paths.
Read first (only): docs/features/Phase 0-Foundation/F-FND-005/US-053-01-resolve-limits/US-053-01-resolve-limits.md (Gherkin blocks + Alternative flows) and F-FND-005-limits-registry.md (AC-053-1/4 + Test plan unit lines).
Do exactly:
1. Add unit tests to src/wa.application.unit using faked IPlansStore/IFlagsStore plus a fake clock — no Testcontainers for resolution logic:
   - per-plan seed resolution: Resolve("free"), Resolve("pro"), Resolve("business") each return exactly their Part-2 constant values (AC-053-1);
   - guest requests resolve to Free's limits without an account lookup (FR-053-6);
   - unknown plan fails with the typed error mapping to 404 PLAN_UNKNOWN (AC-053-4);
   - DB down on cache miss fails with a typed error and no silent fallback to hardcoded defaults (EC-053-3).
Done when: all resolution unit tests pass — these are the first half of the T-005 9/9 suite (the override/TTL half is T-053-06).
Constraints: fakes + fake clock only; assert exact Part-2 values, not "some value".
```
