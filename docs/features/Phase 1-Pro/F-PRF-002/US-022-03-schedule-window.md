# US-022-03 — Schedule within a sane window

**Feature:** F-PRF-002 — Scheduled Send | **Status:** pending

---

**Story:** As a Pro user, I want the schedule picker to validate a 5-minute-to-14-day window, so that I can't pick a time that's already past or absurdly far out.
**Actor:** Pro/Business user.
**Goal:** inline validation on submit: `now + 5 min` ≤ time ≤ `now + 14 days`; out-of-range → named error.

## Preconditions

- User on Pro/Business, link screen with "Schedule" selected.

## Happy path

1. User picks a time inside the window → accepted, stored as UTC.
2. Out-of-range → inline validation naming the window (no submit, no API round-trip for the common case).

## Alternative flows

- **API-level check**: same window re-validated server-side → `SCHEDULE_OUT_OF_WINDOW` (defense in depth, F-TRF-013 screen).
- **Boundary values**: exactly +5 min and exactly +14 days are valid (inclusive).

## Acceptance criteria

```gherkin
Given I pick +2 min
When I submit
Then the form rejects with the 5-minute minimum named

Given I pick +30 days
When I submit
Then the form rejects with the 14-day maximum named

Given I pick exactly +5 min or +14 days
When I submit
Then it is accepted
```

## Edge cases

- Client clock skew: server re-validates with server time (EC defense).
- DST at the picked time: stored UTC (EC-022-6, documented).

## UI notes

- datetime-local input; inline error `--danger` under the field, window text verbatim from the plan.

## Technical notes

- Window is a constant (5 min / 14 days) read from config (never a literal — TA-0.2 rule).
- Validation in both the form (client) and `SendTransferCommand` (server, FR-022-2).

## Links

- Feature: `PRF-002-scheduled-send.md` (FR-022-2, AC-022-2/3)
- Architecture: TA-4.1.3
- Related: F-TRF-013 (error screen)
