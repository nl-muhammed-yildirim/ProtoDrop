# US-019-04 — Get three billing emails before pausing

**Feature:** F-BIL-002 — Stripe Subscription Lifecycle | **Status:** pending

---

**Story:** As a Pro customer whose card failed, I want three billing emails (0 / 3 / 7 days) before my subscription pauses, so that I have a fair chance to fix my card before losing Pro.
**Actor:** paying customer with a failed payment.
**Goal:** a predictable, documented escalation from "card failed" to "paused", with no silent loss of the plan.

## Preconditions

- Active Pro subscription; Stripe fires `invoice.payment_failed`.

## Happy path

1. `invoice.payment_failed` → subscription marked `past_due`; email #1 sent immediately (0 d).
2. Email #2 at +3 d, email #3 at +7 d (the "billing" sequence).
3. After the sequence the subscription is **paused** → grace begins (US-019-05).

## Alternative flows

- **Card fixed before day 3**: Stripe fires `invoice.paid` → sequence cancelled, `active` restored, no pause.
- **Canceled subscription**: sequence not started (only `active`/`trialing` get emails — EC-019-2).

## Acceptance criteria

```gherkin
Given my card fails
When the billing sequence runs
Then I receive emails at 0, 3, and 7 days
And then the subscription is paused

Given I fix my card on day 2
When the next invoice succeeds
Then the remaining emails are cancelled and I stay on Pro
```

## Edge cases

- Duplicate `invoice.payment_failed` (redelivery) → emails idempotent per invoice (dedup by event ID + invoice ID).
- Email outage during the sequence → `f-email` retry/DLQ (F-TRF-006 semantics), not lost.

## UI notes

- Banners: "Your card didn't go through — we'll email you before pausing" (past_due), "You have {n} days to fix your billing" (grace).
- Emails via `f-email` with `billing_payment_failed` ×3 templates; `From = no-reply@{domain}` (D-04).

## Technical notes

- Sequence tracked via `Subscription` status timestamps; scheduled by the 15-minute `f-expire` pass (no new function, TA-6.2).
- Emails: `billing_payment_failed` (×3), reusing the `f-email` pipeline (FR-019-7).

## Links

- Feature: `BIL-002-stripe-lifecycle.md` (FR-019-2, FR-019-7, AC-019-2)
- Architecture: TA-6.2, TA-6.7, TA-3.2
- Related: F-TRF-006 (email pipeline), US-019-05 (grace)
- Decisions: D-04
