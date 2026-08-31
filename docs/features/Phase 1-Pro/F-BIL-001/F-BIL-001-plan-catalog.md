# F-BIL-001 — Plan Catalog

**Priority:** P0 (for Phase 1) | **Phase:** 1 — Pro
**Spec source:** `02-feature-plan.md` F-BIL-001 (outline FR-001-1…4 → remapped below as FR-018-*) | **Architecture:** TA-3.2 (`Plan`), TA-3.4, TA-4.2a
**Milestone tasks:** Phase 1 backlog (not yet created)

---

## Description

Plans are **data, not code**. The `Plan` table (TA-3.2) carries Free / Pro / Business as rows; each row defines every limit constant (Appendix A) via `LimitsJson`, the feature toggles (ads, analytics, branding, scheduling, SSO) via `FeaturesJson`, and the prices (monthly / annual, per-seat for Business) via `PriceJson`. Adding a plan is a new row — no code branch, no deploy. A single resolver (`ILimitsProvider`, TA-3.4) hands every feature its plan's limits per request, so limits live in exactly one place and plan changes take effect without touching active transfers.

**Actors:** any user (their plan determines their limits), operator (launches/edits plans), developer (never branches on plan identity).
**Value:** pricing is a product lever that can move without an engineering release — the precondition for everything else in Phase 1 (billing, branding, scheduling, analytics, ads).

## Functional requirements

| ID | Requirement |
|---|---|
| FR-018-1 | Plans: **Free**, **Pro**, **Business** (D-05, default-accepted). Each plan defines all limit constants (Appendix A) + feature toggles (ads on/off, analytics, branding, scheduling). |
| FR-018-2 | Plans are data: `Plan` table (TA-3.2) seeded by migration. Adding a plan = a new row with validated `LimitsJson` / `FeaturesJson` / `PriceJson`. No application code branches on plan identity. |
| FR-018-3 | Plan prices are per-plan data: monthly and annual price, per-seat for Business (seats = users). Checkout (F-BIL-002) reads prices from the plan row — never from code. |
| FR-018-4 | Plan changes (user plan switch, limit edits via flags) apply at the **next finalize** (new transfers), not retroactively to active transfers. Active transfers keep their send-time limits. |
| FR-018-5 | Every plan row resolves to a `LimitsRecord` (TA-3.4) consumed by F-TRF-007 and F-BIL-003. Free defaults are the Part 2 constants; Pro/Business values pending D-07 are seeded as Appendix-A placeholders (documented as pending). |
| FR-018-6 | `AppUser.PlanId` defaults to Free (FK, TA-3.2). Guests resolve as Free (F-BIL-003-4). |

## Acceptance criteria

```gherkin
AC-018-1: Fresh database seed
  Then Plan contains exactly Free, Pro, Business
  And a new AppUser has PlanId = Free

AC-018-2: A plan limit is edited via flag override
  When ILimitsProvider resolves that plan after the 30 s cache
  Then the new value is returned without a deploy

AC-018-3: A user's plan is changed while they have active transfers
  Then active transfers keep their send-time limits
  And the next finalize uses the new plan's limits

AC-018-4: A checkout session is created for Pro
  Then the Stripe session uses the prices from the Pro plan row (monthly or annual per the user's toggle)
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-018-1 | Pro/Business limit values pending D-07 | Seeded with Appendix-A placeholders (20 GB, 30 d, …), marked "pending D-07" in admin; code never branches on TBD |
| EC-018-2 | Invalid JSON in a plan row | Validated against the schema (limits keys, feature bools, price shape) before commit; a bad row keeps the old value (validate-then-write, same rule as F-TRF-011-4) |
| EC-018-3 | Unknown plan code in `Resolve(code)` | Problem+JSON `UNKNOWN_PLAN` (defensive — plan codes are a seeded, closed set) |
| EC-018-4 | Deleting a plan that still has users | Blocked in admin until those users are migrated (no dangling `AppUser.PlanId`) |

## UI notes (UI-Reference §5.7)

- Plan screen renders plans **from data**: plan name, price with monthly/annual toggle, feature bullets derived from `FeaturesJson`, limit summary ("5 GB per transfer · 7 days · 100 downloads").
- Launching a new plan requires no UI change; a new row appears on the plan screen automatically.

## Technical notes

- `Plan` DDL is authoritative in TA-3.2; seed migration creates the three rows + Free default (T-004 exit check covers the seed).
- `PriceJson` shape: `{ "currency": "USD", "monthly": { "amount": 900, "stripePriceId": "price_…" }, "annual": { "amount": 9000, "stripePriceId": "price_…" }, "perSeatMonthly": null }` — amounts in minor units; Business sets `perSeatMonthly`. Stripe Price objects are created manually by the operator per plan row (runbook step; IDs stored in `PriceJson`).
- `LimitsRecord` + `ILimitsProvider` (TA-3.4, T-005): plan row is the base, `FeatureFlag` keys `limits.{plan}.*` override individual keys, 30 s TTL.
- `FeaturesJson` shape: `{ "ads": bool, "scheduling": bool, "analytics": bool, "branding": bool, "sso_scim": bool }`.
- `Resolve(null | "free")` → Free record for guests (F-BIL-003-4).

## Test plan

- Unit: seed migration produces exactly Free/Pro/Business; `Resolve` per plan; flag override wins; `-1` = ∞ handling; JSON schema validation (bad key / bad type); `UNKNOWN_PLAN`.
- Integration: AC-018-3 (plan switch → next finalize uses new limits, active transfers unchanged); AC-018-4 (checkout session price comes from plan row — exercised with F-BIL-002).
- E2E: plan screen renders all three plans with prices and feature bullets from data (no hardcoded copy).

## User stories

| ID | Story | File |
|---|---|---|
| US-018-01 | Plans are data, not code | `US-018-01-plans-are-data.md` |
| US-018-02 | Always resolve my plan's limits | `US-018-02-resolve-plan-limits.md` |
| US-018-03 | Plan prices live with the plan | `US-018-03-plan-prices.md` |
| US-018-04 | Plan changes apply at my next transfer | `US-018-04-apply-at-finalize.md` |
| US-018-05 | Feature availability follows my plan | `US-018-05-plan-feature-toggles.md` |
