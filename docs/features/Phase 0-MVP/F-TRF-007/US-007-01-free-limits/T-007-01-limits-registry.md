# T-007-01 — Limits Registry: LimitsRecord, ILimitsProvider, caches (T-005)

**Story:** US-007-01 | **Spec:** FR-007-1/5, TA-3.4/TA-13.2 | **Size:** M
**Depends on:** T-004 (domain + WaDbContext — `Plan.LimitsJson`, `FeatureFlag` rows seeded)

---

## Context to read (only these)

- `US-007-01-free-limits.md` → happy path + Technical notes
- `../../F-TRF-007-limits.md` → FR-007-1/5 + Edge cases (EC-007-1/4) + Test plan (unit line)

## Instructions

1. Add the **`LimitsRecord`** POCO in `wa.domain` with all Free defaults: `MAX_TRANSFER_SIZE` 5 GB, `MAX_SINGLE_FILE` 5 GB, `MAX_ZIP_SIZE` 4 GB, `RETENTION_DAYS` 7, `GRACE_DAYS` 3, `MAX_DOWNLOADS` 100, `MAX_EMAILS` 20, `STORAGE_QUOTA_FREE` 5 GB, `ACTIVE_TRANSFERS_MAX_FREE` 20 (Appendix A).
2. Add **`ILimitsProvider.Resolve(planCode)`**: reads the plan's `LimitsJson`, overlays any `FeatureFlag` overrides (`limits.{plan}.*`, TA-13.2), returns a resolved record.
3. Caching: **`PlansCache` + `FlagsCache` with 30 s TTL** (TA-3.4) — no Redis (ADR-008); each replica caches up to 30 s, divergence accepted and documented.
4. `-1` encodes ∞ (Business plan): resolution must surface it as a sentinel the UI/handlers can test for (e.g. `IsInfinite()`), never as a raw negative byte count.

## Exit check

- [ ] Unit: resolution per plan returns the seeded values; flag override wins over `LimitsJson`
- [ ] TTL expiry honored: after 30 s a changed flag value is picked up (test fakes the clock)
- [ ] `-1` = ∞ handling: Business `MAX_DOWNLOADS` resolves to an infinite sentinel

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.domain + WaDbContext in place, Plan/FeatureFlag seeded).
Task T-007-01 — implement the Limits Registry (milestone task T-005).
Read first (only): docs/features/Phase 0-MVP/F-TRF-007/US-007-01-free-limits/US-007-01-free-limits.md (happy path + Technical notes) and F-TRF-007-limits.md (FR-007-1/5).
Do exactly:
1. Add the LimitsRecord POCO in wa.domain with all Free defaults: MAX_TRANSFER_SIZE 5 GB, MAX_SINGLE_FILE 5 GB, MAX_ZIP_SIZE 4 GB, RETENTION_DAYS 7, GRACE_DAYS 3, MAX_DOWNLOADS 100, MAX_EMAILS 20, STORAGE_QUOTA_FREE 5 GB, ACTIVE_TRANSFERS_MAX_FREE 20 (Appendix A).
2. Add ILimitsProvider.Resolve(planCode): reads the plan's LimitsJson, overlays any FeatureFlag overrides (limits.{plan}.*, TA-13.2), returns a resolved record.
3. Cache with PlansCache + FlagsCache at 30 s TTL (TA-3.4) — no Redis (ADR-008); each replica caches up to 30 s, divergence accepted and documented.
4. Keep -1 as the infinity sentinel (Business plan): resolution must surface it so UI/handlers can test IsInfinite() — never a raw negative byte count.
Done when: T-005's exit check holds — unit tests prove per-plan resolution, flag override wins over LimitsJson, TTL expiry is honored with a faked clock, and -1 resolves to an infinity sentinel.
Constraints: wa.domain stays dependency-free (POCO only); the provider lives in wa.application/infrastructure; no literals in later enforcement paths — everything reads from this record (T-007-02).
```
