# T-053-02 — IPlansStore / IFlagsStore ports + infrastructure adapters

**Story:** US-053-01 | **Spec:** FR-053-5, AC-053-4 (PLAN_UNKNOWN data path) | **Size:** M
**Depends on:** T-052-04 (seeded plans/flags rows exist to read), T-053-01 (LimitsRecord shape)

---

## Context to read (only these)

- `../../F-FND-005-limits-registry.md` → FR-053-5 + Technical notes
- `US-053-01-resolve-limits.md` → Alternative flows

## Instructions

1. Add ports **`IPlansStore`** and **`IFlagsStore`** to `wa.application` (the domain never reads the DB — TA-0.2 rule 7).
2. Add their adapters in `wa.infrastructure/Persistence` reading the seeded `Plan` / `FeatureFlag` rows.
3. Unknown plan codes surface as a typed failure mapping to **404 `PLAN_UNKNOWN`** (AC-053-4) — no silent defaults.

## Exit check

- [ ] Ports live in wa.application, adapters in wa.infrastructure (no DB reads anywhere else)
- [ ] Reading an unknown plan code yields the typed PLAN_UNKNOWN failure, not a null/empty record

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; WaDbContext + seeded Plan/FeatureFlag rows in place).
Task T-053-02 — add the store ports and adapters.
Read first (only): docs/features/Phase 0-Foundation/F-FND-005/F-FND-005-limits-registry.md (FR-053-5 + Technical notes) and US-053-01-resolve-limits.md (Alternative flows).
Do exactly:
1. Add ports IPlansStore and IFlagsStore to src/wa.application — the domain never reads the DB (TA-0.2 rule 7).
2. Add their adapters in src/wa.infrastructure/Persistence reading the seeded Plan and FeatureFlag rows via WaDbContext.
3. An unknown plan code must surface as a typed failure mapping to 404 PLAN_UNKNOWN (AC-053-4) — never null, never an empty record.
Done when: ports live in wa.application, adapters in wa.infrastructure/Persistence, and reading an unknown plan yields the typed PLAN_UNKNOWN failure.
Constraints: keep the port surface minimal — only what limit resolution needs; no caching yet (T-053-03 owns the caches).
```
