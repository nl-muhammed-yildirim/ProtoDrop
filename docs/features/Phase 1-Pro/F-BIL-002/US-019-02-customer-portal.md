# US-019-02 — Manage my subscription myself

**Feature:** F-BIL-002 — Stripe Subscription Lifecycle | **Status:** pending

---

**Story:** As a Pro subscriber, I want to self-serve my subscription — cancel, update my card, change billing details — in Stripe's Customer Portal, so that I never have to email support.
**Actor:** paying customer.
**Goal:** one **Manage billing** button opens the Stripe-hosted portal; every action there lands back in our mirror via webhooks.

## Preconditions

- User has an active subscription (Stripe Customer + `client_reference_id` on file).

## Happy path

1. **Manage billing** → `GET /billing/portal` → Stripe billing-portal session.
2. User cancels / updates card / changes details in Stripe's UI.
3. Stripe fires the corresponding webhook → mirror updated (US-019-03).

## Alternative flows

- **Cancel**: Stripe cancels at period end (FR-019-3); the mirror follows on `updated`/`canceled`.
- **Update card**: no plan change; mirror `Subscription.UpdatedAt` refreshes.

## Acceptance criteria

```gherkin
Given I am on Pro
When I open Manage billing
Then the Stripe portal loads for my customer
And changes I make there appear in my account after the webhook
```

## Edge cases

- Portal opened with no subscription → falls back to **Upgrade** (creates the customer).
- Webhook lost for a portal change → Stripe is truth; a re-fetch on conflict heals it (EC-019-4).

## UI notes

- **Manage billing** secondary button (plan screen) when subscribed; **Upgrade** hidden.
- Portal is Stripe-hosted (no custom cards / cancellation form in our UI).

## Technical notes

- `GetPortalSessionQuery` (TA-4.2a `Billing/`, endpoint 18).
- Customer created idempotently on first checkout (`client_reference_id` → Stripe Customer lookup before create).

## Links

- Feature: `BIL-002-stripe-lifecycle.md` (FR-019-1)
- Architecture: TA-7.5, TA-4.2a
- Related: US-019-03 (mirror), F-BIL-001
