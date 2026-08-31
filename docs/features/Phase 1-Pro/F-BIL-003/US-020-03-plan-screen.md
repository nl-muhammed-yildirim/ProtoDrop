# US-020-03 — See my limits and usage

**Feature:** F-BIL-003 — Plan Enforcement | **Status:** pending

---

**Story:** As a signed-in user, I want a plan screen that shows my current plan, how much of each limit I've used, and an upgrade CTA — so that I can see exactly where I stand.
**Actor:** signed-in user (any plan).
**Goal:** a live "used vs. allowed" meter for storage and active transfers, plus one upgrade CTA.

## Preconditions

- User signed in; plan + usage data available.

## Happy path

1. Plan screen shows "Current plan: {name}".
2. Two meters: **storage** used/allowed, **active transfers** used/allowed.
3. **Upgrade** CTA (absent for Business, where meters show ∞ where applicable).

## Alternative flows

- **Business**: no upgrade CTA; ∞ shown where applicable.
- **Free at limit**: the meter is full; the upgrade CTA is the highlighted action.

## Acceptance criteria

```gherkin
Given I am signed in
When I open the plan screen
Then it shows my current plan, storage used/allowed, and active transfers used/allowed
And an upgrade CTA (absent for Business)
```

## Edge cases

- Meter staleness: storage from live scoped query (`SUM(TotalBytes)` active), not a cache — a user just under quota sees the real number.
- Active transfers = `COUNT(*) WHERE Owner=me AND Status=1`.

## UI notes

- UI-Reference §5.7: plan card + meters + **Upgrade** primary (→ F-BIL-002 Checkout).
- Meters: filled bar in `--accent`, used/allowed in `--fs-small`; 100% in `--warning`/`--danger`.

## Technical notes

- `storage_bytes_active` (TA-10.2) + scoped `SUM` for storage; `COUNT` for active transfers.
- Plan data from `PlanContext` (no per-feature plan reads).

## Links

- Feature: `BIL-003-plan-enforcement.md` (FR-020-3, AC-020-3)
- Related: F-BIL-002 (upgrade CTA), F-TRF-007 (storage quota)
- Design: UI-Reference §5.7
