# F-BIL-003 — Plan Enforcement

**Priority:** P0 (for Phase 1) | **Phase:** 1 — Pro
**Spec source:** `02-feature-plan.md` F-BIL-003 (outline FR-003-1…3 → remapped below as FR-020-*) | **Architecture:** TA-3.4, TA-4.2a, TA-4.1.3, TA-13.2
**Milestone tasks:** Phase 1 backlog (not yet created)

---

## Description

The plan is resolved **once, at the start of the request**, by a single middleware step that attaches a `PlanContext` (limits + feature toggles) to the request scope. Every feature — upload, send, scheduling, branding, analytics, ads — reads from that context and **never** branches on plan identity themselves. Downgrading a user with over-quota transfers never kills live links: active transfers finish their natural life; only **new** sends are blocked with a clear, actionable message. The plan screen shows exactly what the user has used versus what their plan allows, with one upgrade CTA.

**Actors:** any user (enforced), operator (watches plan meters in admin), developer (one place to add a plan check).
**Value:** monetization without fragmentation — limits can't drift between features, and the "paywall" moment names the number and the fix.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-020-1 | Single middleware resolves `User → Plan → Limits + Features` at request start (`PlanContext` in the request scope). All features read from it — **no scattered plan checks** in commands/queries. |
| FR-020-2 | Over-quota active transfers on downgrade: allowed to finish their current life (they expire naturally); **new** sends blocked with "Upgrade to keep sending." — `PlanContext`'s limits come from the current plan at send time. |
| FR-020-3 | Plan screen: current plan, limits used vs. allowed (storage, active transfers), upgrade CTA (UI-Reference §5.7). |
| FR-020-4 | Guest resolution: `Resolve(null)` → Free record. Guests are subject to per-transfer limits at draft/finalize/send (F-TRF-007 enforcement points); account-only limits (storage quota, active transfers) apply at account finalize. |
| FR-020-5 | Feature availability is part of the plan, not an extra flag: scheduling (F-PRF-002), branding (F-PRF-001), analytics (F-PRF-003), ads (F-PRF-004) read `PlanContext.Features` — no `if (plan == Pro)` anywhere. |
| FR-020-6 | Enforcement points (single list, mirroring F-TRF-007): `CreateDraftCommand` (per-transfer size), `FinalizeTransferCommand` (storage quota, active transfers), `SendTransferCommand` (re-check + `MAX_EMAILS`), `ScheduleSend` (F-PRF-002 window). |

## Acceptance criteria

```gherkin
AC-020-1: A Free user with 21 active transfers tries to send
  Then the send is rejected with the active-transfers limit named
  And active transfers are untouched

AC-020-2: A Pro user downgrades to Free with 100 active transfers
  Then the active transfers keep working
  And new sends are blocked with "Upgrade to keep sending."

AC-020-3: The plan screen renders for any plan
  Then it shows used vs. allowed for storage and active transfers
  And an upgrade CTA (absent for Business)

AC-020-4: A command references `AppUser.PlanId` directly
  Then the code review fails (no scattered plan checks — FR-020-1)
  (verified by lint rule / code-review checklist)

AC-020-5: A guest finalizes a transfer
  Then Free per-transfer limits apply and account-only limits do not
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-020-1 | Plan flag changed mid-flight | Same semantics as F-TRF-007 EC-007-1: 30 s cache, new resolutions only |
| EC-020-2 | Over-quota user upgrades back | New sends immediately unblocked; no re-check of active transfers needed (they were never blocked) |
| EC-020-3 | `PlanContext` on a request for a deleted user | Resolves to last-known plan (user row soft-deleted); transfer itself is already re-homed (F-TRF-008-6) |
| EC-020-4 | Guest vs. user limit mismatch on upgrade | Guest draft created under Free → user finalizes under Pro: the draft keeps Free per-transfer validation from creation time (documented, same as EC-007-1) |

## UI notes (UI-Reference §5.7)

- Plan screen (signed-in only): "Current plan: {name}" card + two meters (storage used/allowed, active transfers used/allowed) + **Upgrade** primary (→ F-BIL-002 Checkout).
- Paywall states reuse the "limit reached" pattern (F-TRF-007): exact number + action, e.g. "You have 20 active transfers (limit 20). Upgrade to keep sending."
- Business: no upgrade CTA; meters show ∞ where applicable.

## Technical notes

- `PlanContext` (in `wa.application`): `{ Limits: LimitsRecord, Features: PlanFeatures, PlanCode }`; populated by `PlanContextMiddleware` before MediatR dispatch (thin-endpoint rule, TA-4.2a — commands receive it via `IRequest` metadata / scope service, never query `Plan` themselves).
- Enforcement codes (closed list, TA-4.1.3): `TRANSFER_SIZE_EXCEEDED`, `STORAGE_QUOTA_EXCEEDED`, `ACTIVE_TRANSFERS_EXCEEDED` (new), `EMAILS_EXCEEDED` (existing behavior, F-TRF-007).
- Meters: storage from `storage_bytes_active` (TA-10.2) + `SUM(TotalBytes)` scoped query; active transfers = `COUNT(*) WHERE Owner = me AND Status IN (1)`.
- Feature read: `PlanContext.Features.scheduling` etc. (F-BIL-001 `FeaturesJson`); the `feature.*` flag keys (TA-13.2) remain for **global** kill-switches only (e.g. `feature.scheduling = false` for everyone).

## Test plan

- Unit: middleware resolution (user, guest, unknown-plan); feature flag kill-switch precedence over plan row; "no scattered plan checks" helper (assert commands take `PlanContext`).
- Integration: AC-020-1, AC-020-2 (downgrade with active transfers → send blocked), AC-020-4 (via enforced code-review checklist), AC-020-5 (guest draft).
- E2E: plan screen meters render from live data; upgrade flow unlocks a previously blocked send.

## User stories

| ID | Story | File |
|---|---|---|
| US-020-01 | One place decides my plan | `US-020-01-plan-middleware.md` |
| US-020-02 | Keep my live transfers when I downgrade | `US-020-02-overquota-grace.md` |
| US-020-03 | See my limits and usage | `US-020-03-plan-screen.md` |
| US-020-04 | Feature availability follows my plan | `US-020-04-plan-gates-features.md` |
