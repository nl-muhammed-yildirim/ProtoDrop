# US-018-03 — Plan prices live with the plan

**Feature:** F-BIL-001 — Plan Catalog | **Status:** pending

---

**Story:** As a product owner, I want each plan's monthly/annual price (and per-seat pricing for Business) stored on the plan row, so that the Stripe checkout always bills what the plan says — and changing a price is a data change.
**Actor:** product owner (sets prices), paying customer (is billed), operator (manages Stripe Price objects).
**Goal:** `PriceJson` on `Plan` is the single price source for F-BIL-002.

## Preconditions

- `Plan` rows exist; Stripe account in test mode (D-12, open until confirmed).

## Happy path

1. `PriceJson` holds `{ currency, monthly { amount, stripePriceId }, annual { amount, stripePriceId }, perSeatMonthly? }` — amounts in minor units.
2. `CreateCheckoutSessionCommand` (F-BIL-002) reads the price + Stripe Price ID from the row.
3. The plan screen renders prices from the same row.

## Alternative flows

- **Business**: `perSeatMonthly` set; seats = users (F-BIL-001 outline FR-001-3).
- **Price change**: operator creates a new Stripe Price object, updates `PriceJson` (data change, no deploy).

## Acceptance criteria

```gherkin
Given the pro plan row has monthly 900 and annual 9000 (minor units)
When a user starts checkout with the annual toggle
Then the Stripe session references the annual stripePriceId from the row

Given the plan screen loads
Then it shows exactly the row's prices — nothing hardcoded
```

## Edge cases

- Missing `stripePriceId` (row not yet wired to Stripe): checkout disabled with "Setting up — try again soon" (operator fix = set the ID).
- Currency: single currency per row at launch (USD); multi-currency is a later decision, not this story.

## UI notes

- Price line on plan cards: "$9/mo" or "$90/yr" per the toggle; Business shows "per seat".

## Technical notes

- `PriceJson` shape in F-BIL-001 technical notes; Stripe Price objects are operator-managed (runbook step), IDs persisted on the row.
- Checkout is Stripe-hosted (F-BIL-002) — the price is never computed in app code.

## Links

- Feature: `BIL-001-plan-catalog.md` (FR-018-3, AC-018-4)
- Architecture: TA-3.2, TA-7.5
- Related: F-BIL-002 (checkout), UI-Reference §5.7
- Decisions: D-07 (values), D-12 (Stripe account)
