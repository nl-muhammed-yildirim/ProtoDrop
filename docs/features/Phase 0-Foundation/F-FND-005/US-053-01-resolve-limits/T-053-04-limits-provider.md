# T-053-04 — LimitsProvider: Resolve with override layering + guest → Free

**Story:** US-053-01 | **Spec:** FR-053-2/3/6, AC-053-1/2/4 | **Size:** M
**Depends on:** T-053-03 (caches in place)

---

## Context to read (only these)

- `US-053-01-resolve-limits.md` → happy path + Gherkin blocks + Alternative flows
- `US-053-02-flag-override.md` → first Gherkin block (override semantics)
- `../../F-FND-005-limits-registry.md` → FR-053-2/3/6 + AC-053-1/2/4

## Instructions

1. Add **`LimitsProvider`** implementing **`ILimitsProvider.Resolve(planCode)`** in `wa.application/Limits/`:
   - base = the plan's seeded `LimitsJson` (deserialized into `LimitsRecord`)
   - each existing `limits.<plan>.<key>` flag overrides **only its one field** — every other field, and both other plans, stay unchanged (AC-053-2)
   - `feature.*` flags and unknown key segments are ignored by limit resolution (FR-053-3 / EC-053-1)
2. Guest requests resolve to the **Free** plan's limits — no account required (FR-053-6).
3. Unknown plans → typed failure mapping to 404 `PLAN_UNKNOWN` (AC-053-4); DB down on cache miss → typed failure, **no silent fallback to hardcoded defaults** (EC-053-3 — hardcoded = golden rule 2 violation).

## Exit check

- [ ] `Resolve("free")`, `Resolve("pro")`, `Resolve("business")` each return exactly their Part-2 constant values (AC-053-1)
- [ ] A flag setting `limits.free.maxTransferSize` to 10 GB changes only that one field on free (AC-053-2)
- [ ] Guest resolution returns Free's limits without an account lookup

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; caches + store ports in place).
Task T-053-04 — implement limit resolution.
Read first (only): docs/features/Phase 0-Foundation/F-FND-005/US-053-01-resolve-limits/US-053-01-resolve-limits.md (happy path + Gherkin blocks + Alternative flows), US-053-02-flag-override.md (first Gherkin block for override semantics), and F-FND-005-limits-registry.md (FR-053-2/3/6 + AC-053-1/2/4).
Do exactly:
1. Add LimitsProvider implementing ILimitsProvider.Resolve(planCode) in src/wa.application/Limits: base = the plan's seeded LimitsJson deserialized into LimitsRecord, then each existing limits.<plan>.<key> flag overrides only its one field — every other field of that plan and both other plans stay unchanged (AC-053-2).
2. Ignore feature.* flags and unknown key segments during limit resolution (FR-053-3 / EC-053-1).
3. Guest requests resolve to the Free plan's limits with no account lookup (FR-053-6); unknown plans fail with the typed error mapping to 404 PLAN_UNKNOWN (AC-053-4).
Done when: AC-053-1, AC-053-2, and AC-053-4 all hold against real seeded data — per-plan seeds resolve exactly, an override touches precisely one field, and unknown plans fail loudly.
Constraints: no literal limit values in code (golden rule 2) — every value comes from seeds or flags; DB down on cache miss is a typed failure, never a hardcoded fallback (EC-053-3).
```
