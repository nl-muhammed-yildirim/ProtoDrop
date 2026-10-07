# F-FND-005 — Limits Registry

**Priority:** P0 (foundation) | **Phase:** 0 — Foundation (M0)
**Spec source:** `Milestone-Backlog.md` T-005, TA-3.4 | **Architecture:** TA-3.4, TA-13.2
**Milestone tasks:** T-005

---

## Description

Every limit in the product — transfer size, download caps, retention days, storage quota — lives in one place: the Limits Registry. `LimitsRecord` is a BCL-only POCO in `wa.domain`; `ILimitsProvider.Resolve(planCode)` answers "what are this plan's effective limits?" by layering flag overrides (`limits.<plan>.*`) over the plan's seeded `LimitsJson`, with a 30-second in-memory cache so reads stay cheap and edits propagate fast. Golden rule 2 ("no literal limit values in code") is only enforceable because this registry exists — from T-009 on, every feature reads limits through it instead of hardcoding.

**Actors:** developer/AI session (calls `Resolve`), SuperAdmin (edits overrides via F-XCT-001's admin screen), every plan-gated feature (F-TRF-007, F-BIL-*).
**Value:** limits are data. A 3 a.m. fix to "max transfer size" is one flag edit, not a deploy; guests resolve to Free automatically.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-053-1 | `LimitsRecord` (in `wa.domain`) holds every constant from Feature Plan Appendix A as a BCL-only POCO with camelCase JSON property names matching the seeded `LimitsJson`. |
| FR-053-2 | `ILimitsProvider.Resolve(planCode)` returns the effective limits for a plan: base = the plan's `LimitsJson` (seeded), then each existing `limits.<plan>.<key>` flag overrides **only its one field**. Unknown plans → 404 `PLAN_UNKNOWN`. |
| FR-053-3 | Feature-gate flags (`feature.*`) and unknown key segments are ignored by limit resolution — gates are read separately (F-XCT-001 territory). |
| FR-053-4 | `PlansCache` + `FlagsCache` hold plan rows and flag rows with a **30 s TTL** (`Wa:Flags:TtlSeconds`, TA-3.4 technical constant; injectable clock for tests). Every read goes through the caches. |
| FR-053-5 | Port boundaries keep DB reads out of `wa.domain`: `IPlansStore` / `IFlagsStore` ports live in `wa.application`; adapters in infrastructure (TA-0.2 rule 7). |
| FR-053-6 | Guest requests resolve to the **Free** plan's limits — no account required to know a limit exists (supports F-TRF-001 pre-checks and F-TRF-007 server enforcement). |

## Acceptance criteria

```gherkin
AC-053-1: Limits resolve per plan from seeds
  Given the seeded plans (F-FND-004)
  When I call Resolve("free"), Resolve("pro"), and Resolve("business")
  Then each returns exactly its Part-2 constant values

AC-053-2: A flag override wins, precisely
  Given limits.free.maxTransferSize is seeded at the Free value
  When a FeatureFlag row sets it to 10 GB
  Then within one TTL window Resolve("free").MaxTransferSize is the 10 GB value
  And every other field of free and both other plans are unchanged

AC-053-3: The cache honors its TTL
  Given a cached entry younger than 30 s
  When a second read arrives before expiry
  Then no refetch happens (same instance/row)
  And after 30 s the next read refetches

AC-053-4: Unknown plans fail loudly
  When I call Resolve("enterprise")
  Then the result is a failure mapping to 404 PLAN_UNKNOWN
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-053-1 | A `feature.*` flag or unknown segment appears in the flags table | Ignored by limit resolution (only `limits.<plan>.<key>` keys apply — FR-053-3) |
| EC-053-2 | Flag edited mid-request | In-flight resolutions keep their values; only new resolutions after TTL expiry see the change (documented, matches F-XCT-001 EC-044-2) |
| EC-053-3 | DB down on cache miss | Resolution fails with a typed error; callers map per TA-4.1.3 — no silent fallback to hardcoded defaults (hardcoded = golden rule 2 violation) |

## UI notes

- None at M0 (the admin Flags screen that edits these keys is F-XCT-001 / T-065).

## Technical notes

- `wa.application/Limits/`: `PlansCache`, `FlagsCache` (30 s TTL, injectable `Func<DateTimeOffset>` clock), `LimitsProvider`.
- Unit tests use faked `IPlansStore`/`IFlagsStore` + fake clock — no Testcontainers needed for resolution logic (9/9 in T-005).
- This registry is the M0 half of F-TRF-007 (Free-Tier Limits): enforcement at finalize/send lands with T-020; the *resolution* machinery is here.

## Test plan

- Unit: per-plan seed resolution (free/pro/business); override wins for exactly one field; TTL honored on both caches (fresh entry not refetched, 30 s entry refetched).
- Integration: covered by later tasks that read limits through real stores (T-020 end-to-end).
- Gate: full AGENT.md §4 gate green.

## User stories

| ID | Story | File |
|---|---|---|
| US-053-01 | Ask "what are this plan's limits?" in one call | `US-053-01-resolve-limits.md` |
| US-053-02 | Change a limit and have it apply within 30 seconds | `US-053-02-flag-override.md` |
