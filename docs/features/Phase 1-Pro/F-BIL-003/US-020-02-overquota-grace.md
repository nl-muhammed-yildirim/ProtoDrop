# US-020-02 — Keep my live transfers when I downgrade

**Feature:** F-BIL-003 — Plan Enforcement | **Status:** pending

---

**Story:** As a downgraded user, I want my **active transfers to keep working** even if they now exceed my new plan's quota, and only **new sends** to be blocked with a clear message — so that a downgrade never breaks a link someone is using.
**Actor:** downgraded user, their recipients.
**Goal:** downgrade = "new sends blocked, live ones finish naturally", with the exact limit named.

## Preconditions

- User downgraded (F-BIL-002) with active transfers that exceed the new plan's quota.

## Happy path

1. Downgrade lands; active transfers untouched (send-time limits, F-BIL-001-4).
2. User tries a **new** send over quota → blocked with "Upgrade to keep sending." + the exact number.
3. Live transfers expire naturally; recipients are never affected mid-life.

## Alternative flows

- **Re-upgrade**: new sends immediately unblocked; active transfers never needed re-checking.
- **Guest → user upgrade mid-draft**: draft keeps creation-time validation (EC-020-4, documented).

## Acceptance criteria

```gherkin
Given I downgraded to free with 100 active transfers
When I try a new send
Then it is blocked with the active-transfers limit named
And my active transfers keep working

Given I re-upgrade to pro
When I try the same send
Then it succeeds
```

## Edge cases

- Over-quota active transfers are **not** retroactively expired (they finish their life).
- `ACTIVE_TRANSFERS_EXCEEDED` Problem+JSON (closed code list, TA-4.1.3).

## UI notes

- Paywall state reuses the "limit reached" pattern (F-TRF-007): exact number + **Upgrade** CTA.
- "Upgrade to keep sending." tone — calm, no exclamation points.

## Technical notes

- Enforcement at `FinalizeTransferCommand` / `SendTransferCommand` via `PlanContext` (F-BIL-003-6).
- Code: `ACTIVE_TRANSFERS_EXCEEDED` (new, TA-4.1.3).

## Links

- Feature: `BIL-003-plan-enforcement.md` (FR-020-2, AC-020-1/2)
- Related: F-BIL-001 (send-time limits), F-TRF-007 (limit screens)
- Architecture: TA-4.1.3
