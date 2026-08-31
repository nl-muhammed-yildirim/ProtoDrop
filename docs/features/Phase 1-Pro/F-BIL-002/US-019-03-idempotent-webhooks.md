# US-019-03 — Our records mirror Stripe, exactly once

**Feature:** F-BIL-002 — Stripe Subscription Lifecycle | **Status:** pending

---

**Story:** As the system, I want every Stripe webhook applied **exactly once** (deduplicated by event ID) so that our billing mirror never drifts from Stripe, no matter how many times a webhook is redelivered.
**Actor:** the webhook handler (`HandleStripeWebhookCommand`), operator watching for drift.
**Goal:** Stripe is truth; our `Subscription` rows are a read model; replays are safe.

## Preconditions

- `StripeEvent` table (event-ID PK) and `Subscription` table (TA-3.2) exist.
- Webhook endpoint `POST /billing/webhooks/stripe` (endpoint 19).

## Happy path

1. Webhook arrives → verify `Stripe-Signature`.
2. Insert the event ID into `StripeEvent` (PK = dedup). Loser on concurrent duplicate re-reads and returns 200.
3. Apply the event to `Subscription` + `AppUser.PlanId` (mirror).
4. Unknown event types → 200 OK + logged, no mirror change.

## Alternative flows

- **Duplicate delivery**: second insert collides → re-read → 200 (at-least-once, deduplicated).
- **Out-of-order**: handler re-fetches the subscription from the Stripe API to heal (Stripe is truth — FR-019-4).

## Acceptance criteria

```gherkin
Given a webhook is delivered twice
When both are processed
Then the mirror is applied exactly once

Given a webhook with an unknown event type
When processed
Then 200 OK, logged, and no mirror change
```

## Edge cases

- Unknown `client_reference_id` (signup race) → retryable → DLQ after 3 (EC-019-1).
- Concurrent duplicates → PK collision path returns 200 (EC-019-6).
- Stripe API down during a heal-fetch → retryable, DLQ on exhaustion.

## UI notes

- Invisible to users; the effect is correct plan state everywhere (plan screen, banners).

## Technical notes

- `HandleStripeWebhookCommand` (TA-4.2a `Billing/`), single entry point for all webhook types (FR-019-5).
- `plan.changed` (TA-5.3) emitted on every mirror transition that affects limits.
- Metric: `dlq_count > 0` alert (existing, TA-10.3).

## Links

- Feature: `BIL-002-stripe-lifecycle.md` (FR-019-2, FR-019-4, FR-019-5, AC-019-3/4)
- Architecture: TA-3.2, TA-4.2a, TA-7.5
- Related: F-BIL-001, US-019-01/02
