# F-BIL-002 — Stripe Subscription Lifecycle

**Priority:** P0 (for Phase 1) | **Phase:** 1 — Pro
**Spec source:** `02-feature-plan.md` F-BIL-002 (outline FR-002-1…4, AC-002-1/2 → remapped below as AC-019-1/2) | **Architecture:** TA-7.5, TA-3.2 (`Subscription`, `StripeEvent`), TA-4.2#18–19, TA-5.3
**Milestone tasks:** Phase 1 backlog (not yet created)

---

## Description

Stripe Billing is the **source of truth for money**; our database is a mirror (a read model for limits and UI). Customers upgrade through Stripe-hosted **Checkout** and self-serve (cancel, update card) in the **Customer Portal**. Every state change arrives as a **webhook**, applied exactly once (idempotent by event ID in `StripeEvent`) through one MediatR command. A 14-day **grace period** cushions a failed payment before a downgrade to Free. Upgrade is immediate; downgrade happens at period end (Stripe default).

**Actors:** paying customer (upgrades, self-serves), operator (creates Stripe prices, watches webhooks/DLQ), the system (applies plan changes).
**Value:** revenue with minimal ops — the customer never needs to email support for a card problem, and the mirror can never drift silently (event IDs make every webhook replay-safe).

## Functional requirements

| ID | Requirement |
|---|---|
| FR-019-1 | Stripe Billing: **Checkout** for upgrade, **Customer Portal** for self-serve (cancel, update card, billing details). |
| FR-019-2 | Webhooks (must be idempotent by event ID): |
| | • `customer.subscription.created/updated` → plan + seats applied |
| | • `customer.subscription.paused` → **grace**: keep plan for 14 days, then downgrade to Free |
| | • `customer.subscription.resumed` → restore |
| | • `invoice.payment_failed` → 3 billing emails (0 d, 3 d, 7 d), then pause |
| FR-019-3 | Upgrade = immediate; downgrade = end-of-period (Stripe default, no custom logic). |
| FR-019-4 | All billing state is a **mirror of Stripe** — we never trust our copy; Stripe is truth, mirrored via webhooks into `Subscription` (TA-3.2). |
| FR-019-5 | Webhook handling: verify `Stripe-Signature` → dedup by event ID in `StripeEvent` → single MediatR command `HandleStripeWebhookCommand` (TA-4.2a). Unknown event types: 200 OK + logged, no mirror change. |
| FR-019-6 | **Grace:** on `paused` → `Subscription.GraceEndsAtUtc = now + 14 d` (GRACE constant, 14 d for all plans). The 15-minute timer (`f-expire` pass, TA-6.3) downgrades to Free when grace ends: `AppUser.PlanId = Free`, `plan.changed`, "You're back on Free" email. |
| FR-019-7 | Billing emails (payment-failed 0/3/7 sequence, grace ending/ended, upgrade confirmed) reuse the `f-email` pipeline with `billing_*` templates — no new sending path. |
| FR-019-8 | `plan.changed` event (TA-5.3) emitted on every mirror transition that affects limits (`{ userId, fromPlan, toPlan, reason }`). |

## Acceptance criteria

```gherkin
AC-019-1: Free user upgrades
  Then Stripe Checkout completes, plan applied within 60 s, "You're on Pro" banner
  (plan AC-002-1)

AC-019-2: Card fails
  Then 3 emails at 0/3/7 days, then subscription paused
  And after 14-day grace, plan downgrades to Free and a "You're back on Free" email is sent
  (plan AC-002-2)

AC-019-3: The same webhook is delivered twice
  Then the mirror is applied exactly once (StripeEvent dedup)

AC-019-4: Webhook with an unknown event type
  Then 200 OK, event logged, no mirror change

AC-019-5: Stripe API unavailable during checkout creation
  Then Problem+JSON BILLING_UNAVAILABLE, user can retry, no partial mirror
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-019-1 | Webhook for an unknown `client_reference_id` (signup race) | Retryable; after 3 consumer attempts → DLQ + `dlq_count` alert (operator re-plays) |
| EC-019-2 | `invoice.payment_failed` for a canceled subscription | Ignored (email sequence starts only for `active`/`trialing` subscriptions) |
| EC-019-3 | Subscription resumed during grace | `resumed` clears `GraceEndsAtUtc`, restores the plan; any pending "back on Free" email is cancelled |
| EC-019-4 | Out-of-order webhooks (rare, network) | Mirror applies the latest Stripe state; on conflict the handler re-fetches the subscription from the Stripe API (Stripe is truth — FR-019-4) |
| EC-019-5 | User cancels in the portal | Stripe cancels at period end; the mirror follows on `updated`/`canceled` (FR-019-3 — no custom downgrade logic) |
| EC-019-6 | Duplicate webhook delivered concurrently | `StripeEvent` PK collision: the loser re-reads, sees the row, returns 200 (at-least-once with dedup) |

## UI notes (UI-Reference §5.7)

- Plan screen: **Upgrade** primary (→ Stripe-hosted Checkout), **Manage billing** secondary (→ Stripe Customer Portal) when subscribed.
- Dismissible banners (`--bg-elevated`, no exclamation points): "You're on Pro" (after upgrade) · "Your card didn't go through — we'll email you before pausing" (past_due) · "You have {n} days to fix your billing" (grace, counting down).
- No custom receipt screen — Stripe Checkout/Portal handle payment UX.

## Technical notes

- Sequence (TA-7.5): `POST /billing/checkout` → `session.mode = subscription`, `client_reference_id = userId` → webhooks. `GET /billing/portal` → Stripe billing portal session.
- Tables: `Subscription`, `StripeEvent` (TA-3.2). **Test mode** until D-12 is confirmed.
- Stripe Price objects: created manually per plan row (operator runbook step); IDs stored in `PriceJson` (F-BIL-001).
- Grace timer: sub-step inside `f-expire`'s 15-minute pass (TA-6.3) — **no new function**, TA-6.2 inventory stays unchanged. Billing-email schedule (0/3/7 d) tracked via `Subscription` status timestamps, same pass.
- Emails: `billing_payment_failed` (×3), `billing_grace_ending`, `billing_grace_ended`, `billing_upgraded` — via `f-email`, `From = no-reply@{domain}` (D-04).
- Metrics: `plan_changed` per transition; alert on `dlq_count > 0` (existing, TA-10.3).

## Test plan

- Unit: webhook signature verification; event-ID dedup; grace-date math; mirror transition table per event type; email schedule computation.
- Integration: AC-019-1 (Checkout → webhook → plan applied, fake clock ≤ 60 s); AC-019-2 (full failed-card sequence with time travel); AC-019-3 (duplicate delivery → one application); AC-019-4; AC-019-5 (Stripe outage → Problem+JSON, no mirror row).
- E2E: test-mode Checkout round-trip in dev (manual pass, documented in the Phase 1 runbook).

## User stories

| ID | Story | File |
|---|---|---|
| US-019-01 | Upgrade to Pro with a few clicks | `US-019-01-upgrade-checkout.md` |
| US-019-02 | Manage my subscription myself | `US-019-02-customer-portal.md` |
| US-019-03 | Our records mirror Stripe, exactly once | `US-019-03-idempotent-webhooks.md` |
| US-019-04 | Get three billing emails before pausing | `US-019-04-billing-emails.md` |
| US-019-05 | Keep working during the 14-day grace | `US-019-05-grace-downgrade.md` |
| US-019-06 | Be restored when my subscription resumes | `US-019-06-restore-sub.md` |
