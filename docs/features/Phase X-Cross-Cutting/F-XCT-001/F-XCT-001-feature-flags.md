# F-XCT-001 — Feature Flags & Remote Limits

**Priority:** P0 (foundation) | **Phase:** X — Cross-cutting
**Spec source:** `02-feature-plan.md` §6 (F-XCT-001) | **Architecture:** TA-3.4, TA-13.2
**Milestone tasks:** T-065, T-068

---

## Description

Every plan limit and every phase gate (ads, scheduling, analytics, data export, search) lives in the **flag service**: a small, admin-editable registry that answers "what is this plan's limit?" and "is this feature on?" without a deploy. Flags are the kill switch, the experiment knob, and the launch gate in one — which is why the admin's Flags screen is the only place a number like `5 GB` may be changed at 3 a.m. The service is deliberately dumb: one SQL table, one read cache with a 30 s TTL, two resolution methods, an audit trail on every write.

**Actors:** SuperAdmin (edits flags in the admin dashboard), operator (reads the flag list during incidents), all users and features (indirectly — every `ResolveLimits` call and every phase-gated UI surface depends on this).
**Value:** release decoupling. Limits and features change via data, not code; incidents resolve with a flag edit, not a rollback.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-044-1 | The flag service stores flags in the seeded `FeatureFlag` table (TA-13.2): `Key` (VARCHAR 100 PK), `Value` (NVARCHAR 2000, JSON scalar or `true/false`), `Version` (rowversion), `UpdatedBy` (nullable email), `UpdatedAtUtc`. Seeded keys ship with the migration: `limits.free.{maxTransferSize,maxDownloads,retentionDays,maxEmails,storageQuota,activeTransfersMax}`, `limits.pro.*`, `limits.business.*`, `feature.ads`, `feature.scheduling`, `feature.analytics`, `feature.resend`, plus **`feature.search`** (F-XCT-002), **`feature.dataExport`** (F-XCT-004) and **`feature.emailSettings`** (F-XCT-003). |
| FR-044-2 | `LimitsProvider.Resolve(planId)` returns the effective limits for a plan: `Plan.LimitsJson` (seeded defaults) **overridden key-by-key by `limits.<plan>.<key>` flags** that exist. Unknown plans → 404 `PLAN_UNKNOWN`. |
| FR-044-3 | `LimitsProvider.ResolveFlag(key, default)` returns the flag value as JSON with the given default when the key is absent (absent = default, not error). Values are typed per key at read time (int for `limits.*`, bool for `feature.*`). |
| FR-044-4 | In-memory `FlagsCache` with **30 s TTL** (TA-3.4, `Wa:Flags:TtlSeconds`, ADR-008: no Redis). Every read goes through the cache; a write bumps the table row, and the next read (≤ 30 s later) observes the new value. Divergence across replicas ≤ 30 s is accepted and documented. |
| FR-044-5 | Admin API (endpoint 23): `GET /api/v1/admin/flags` lists all seeded keys with current effective values and `updatedAt`; `PUT /api/v1/admin/flags/{key}` validates the JSON shape for the key's known type, upserts, and returns the new value. Unknown key → 404 `FLAG_UNKNOWN` (seeded keys only, per TA-13.2 — no free-form flag sprawl). |
| FR-044-6 | Every `PUT` writes an `admin_action` audit row (`admin.action` telemetry, TA-10.2): actor email, key, old and new values in `DetailsJson` (PII-free — emails of *other* users never appear in flag values, except `feature.*` booleans which carry none). |
| FR-044-7 | **Launch gates:** Phase 1/2 features read their gate from the flag, not from code constants: `feature.ads` (F-PRF-004), `feature.scheduling` (F-PRF-002), `feature.analytics` (F-PRF-003), `feature.search` (F-XCT-002), `feature.dataExport` (F-XCT-004), `feature.emailSettings` (F-XCT-003). Flag **off** → the feature's UI surfaces are hidden and its API returns 404 `FEATURE_DISABLED` (or the documented per-feature behavior). |
| FR-044-8 | Flags are **not** the plan catalog: `Plan` rows (F-BIL-001) define the plan set; flags only override individual limit keys and gate booleans. A flag can never create or delete a plan. |

## Acceptance criteria

```gherkin
AC-044-1: A SuperAdmin edits a limit
  Given limits.free.maxTransferSize is 5368709120
  When the admin PUTs 10737418240 to that key
  Then within 30 s ResolveLimits("free").MaxTransferSize is 10737418240
  And no in-flight transfer changed its send-time limit
  And an admin.action audit row records actor, key, old and new values

AC-044-2: A flag gates a phase feature
  Given feature.dataExport is false
  When a signed-in user requests their data export (F-XCT-004)
  Then the response is 404 with code FEATURE_DISABLED
  And the export screen is not rendered

AC-044-3: The flag list is finite and typed
  When an admin PUTs to limits.free.nonexistent
  Then the response is 404 FLAG_UNKNOWN
  And when they PUT the string "abc" to limits.free.maxTransferSize
  Then the response is 400 FLAG_INVALID with the previous value still effective
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-044-1 | Bad JSON / wrong type on save | Save rejected inline, old value stays live (validates before write) |
| EC-044-2 | Flag edited while a transfer is being finalized | In-flight resolutions keep their values; only new resolutions pick up the change (documented, per EC-007-1 in F-TRF-007) |
| EC-044-3 | Two admins edit the same key concurrently | Last write wins on the row; both edits appear in the audit trail with `UpdatedAtUtc` ordering |
| EC-044-4 | Replica with stale cache (≤ 30 s) | Accepted divergence (ADR-008); the Flags screen says "Changes take effect within 30 seconds." |
| EC-044-5 | `limits.*` flag set to a non-positive number | Treated as 400 `FLAG_INVALID` (limits are positive integers; `0` for `maxDownloads` would be "no downloads ever") |
| EC-044-6 | All seeded flags deleted from the table | Not possible via UI (seeded keys only); if a DBA deletes rows, `ResolveFlag` falls back to the documented default (feature = off, limit = `Plan.LimitsJson` seed) — service degrades to defaults, not to failure |

## UI notes

- Admin → **Flags** (UI-Reference §5.6): two groups — **Limits** (per-plan tables of the seeded `limits.*` keys) and **Features** (booleans with a switch). Per-key save; dirty-state indicator; "Changes are logged." hint; "Changes take effect within 30 seconds." caption.
- The Flags screen shows the *effective* value (flag override vs. plan seed) so an operator can see which values are actually overridden.
- No flag editing outside the admin area; `Wa:AdminEmails` seeded emails only (F-TRF-011).

## Technical notes

- Schema: existing `dbo.FeatureFlag` (TA-13.2) — T-065 adds the seeded `feature.search`, `feature.dataExport`, `feature.emailSettings` keys in the same migration batch pattern as TA-3.7.
- `ILimitsProvider` port in `wa.application`; `FlagsCache` (30 s TTL, `Wa:Flags:TtlSeconds`) in `wa.infrastructure`; resolution = `Plan.LimitsJson` ← flag overrides, built once per TTL window.
- `SetFlagCommand` / `GetFlagsQuery` (already named in the Admin/ command list, TA-4.2); audit via `IAdminAudit` (TA-4.2a).
- Telemetry: `admin_action` on writes only; a `flag_resolved` event is **not** in the TA-10.2 closed list, so flag reads emit no telemetry (names are the contract — no drift).
- Phase-gate contract: gate checks must be a single call (`ResolveFlag("feature.search")`), never a hardcoded `#if` or config literal, so each phase feature can be killed in prod with one click.

## Test plan

- Unit: `ResolveLimits` override precedence (flag over plan seed); `ResolveFlag` absent-key default; type validation for int/bool keys; effective-value computation for `GET /admin/flags`.
- Integration: `PUT` valid/invalid/unknown key (happy + error per endpoint 23, TA-14.1); TTL behavior — write, read (cached old), wait/force-expire, read (new); audit row present with exact old/new values (transactional).
- E2E: set `feature.dataExport` off in staging → export screen absent; on → present (Playwright).
- Exit check: "All seeded keys resolvable; a limit edit is visible to a new finalize within 30 s without restart" (T-068 exit).

## User stories

| ID | Story | File |
|---|---|---|
| US-044-01 | Change a limit without a deploy | `US-044-01-edit-limits.md` |
| US-044-02 | Gate a phase feature with a flag | `US-044-02-gate-feature.md` |
