# US-019-06 — Be restored when my subscription resumes

**Feature:** F-BIL-002 — Stripe Subscription Lifecycle | **Status:** pending

---

**Story:** As a customer who fixed my billing, I want my Pro plan restored automatically when my subscription resumes, so that I get my features back without re-upgrading.
**Actor:** paying customer whose paused subscription resumes.
**Goal:** `customer.subscription.resumed` → plan restored, grace cleared, pending downgrade email cancelled.

## Preconditions

- Subscription was `paused` and is in grace (US-019-05).

## Happy path

1. Stripe fires `customer.subscription.resumed`.
2. Handler clears `Subscription.GraceEndsAtUtc`, restores `AppUser.PlanId = pro`, emits `plan.changed`.
3. Any pending "back on Free" email is cancelled.

## Alternative flows

- **Resumed after downgrade already happened**: re-upgrade path (user pays again) — resume only applies while grace still holds.
- **Resumed without prior pause** (Stripe edge) → treated as `active` refresh, no-op on plan if already Pro.

## Acceptance criteria

```gherkin
Given my subscription is paused in grace
When it resumes
Then I am restored to Pro
And the pending "back on Free" email is cancelled
```

## Edge cases

- Duplicate `resumed` webhook → idempotent (event-ID dedup, US-019-03).
- Resume racing the grace timer → last-writer-wins on `Subscription` row; both audit-logged; timer re-checks grace before downgrading (EC-019-3).

## UI notes

- On restore: "You're back on Pro" banner (no exclamation points, UI-Reference §5.7).

## Technical notes

- Part of `HandleStripeWebhookCommand` (TA-4.2a `Billing/`); `plan.changed` (TA-5.3) with `reason = "resumed"`.
- Email cancellation: check `Subscription.GraceEndsAtUtc IS NULL` before sending (the timer already checks).

## Links

- Feature: `BIL-002-stripe-lifecycle.md` (FR-019-2, AC-019-2)
- Architecture: TA-4.2a, TA-5.3
- Related: US-019-05 (grace), US-019-03 (mirror)
