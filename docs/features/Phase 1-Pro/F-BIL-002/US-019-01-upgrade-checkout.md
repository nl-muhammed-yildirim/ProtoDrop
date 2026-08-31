# US-019-01 — Upgrade to Pro with a few clicks

**Feature:** F-BIL-002 — Stripe Subscription Lifecycle | **Status:** pending

---

**Story:** As a free user who outgrows the free tier, I want to upgrade to Pro through Stripe Checkout so that I'm billed correctly and my limits expand without emailing anyone.
**Actor:** free-tier account user, at the plan screen.
**Goal:** one click on **Upgrade** → Stripe-hosted Checkout → back on Pro within a minute.

## Preconditions

- User is signed in (F-TRF-008) on the free plan.
- Plan row has `PriceJson` with Stripe Price IDs (F-BIL-001, US-018-03).
- Stripe account in test mode (D-12).

## Happy path

1. User clicks **Upgrade** → `POST /billing/checkout` → Stripe `session.mode=subscription`, `client_reference_id=userId` (TA-7.5).
2. User pays on Stripe-hosted Checkout (card fields never touch our pages).
3. Stripe fires `customer.subscription.created` → webhook handler (US-019-03) applies `AppUser.PlanId = pro`.
4. Plan applied within 60 s; "You're on Pro" banner (UI-Reference §5.7).

## Alternative flows

- **Monthly/annual toggle**: user picks annual → session uses the annual `stripePriceId` (F-BIL-001).
- **Upgrade while a transfer is active**: active transfer keeps its send-time limits; new transfers get Pro (F-BIL-001-4, US-018-04).

## Acceptance criteria

```gherkin
Given I am on free and I click Upgrade
When I complete Stripe Checkout
Then my plan is pro within 60 s
And a "You're on Pro" banner is shown

Given I select annual before paying
Then the Stripe session references the annual price from the plan row
```

## Edge cases

- Checkout abandoned: no subscription, still free, no partial mirror row.
- Stripe API unavailable at session creation → Problem+JSON `BILLING_UNAVAILABLE`, retryable (AC-019-5).
- Plan already Pro → button reads **Manage billing** (→ portal), not a second checkout.

## UI notes

- Plan screen: **Upgrade** primary (→ Checkout), monthly/annual toggle; "You're on Pro" banner after.
- No custom payment form — Stripe hosts it (UI-Reference §5.7).

## Technical notes

- `CreateCheckoutSessionCommand` (TA-4.2a `Billing/`, endpoint 18).
- `client_reference_id=userId` lets the webhook find the user (F-BIL-002-5).
- Upgrade = immediate (FR-019-3): plan applied on the `created` webhook, no wait for period end.

## Links

- Feature: `BIL-002-stripe-lifecycle.md` (FR-019-1, FR-019-3, AC-019-1)
- Architecture: TA-7.5, TA-4.2a
- Related: F-BIL-001 (prices), UI-Reference §5.7
- Decisions: D-07, D-12
