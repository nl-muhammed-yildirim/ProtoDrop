# US-018-04 — Plan changes apply at my next transfer

**Feature:** F-BIL-001 — Plan Catalog | **Status:** pending

---

**Story:** As a user whose plan just changed, I want my **active transfers to keep working** and only my **new transfers** to use the new limits — so that a plan change never surprises a live link or an in-flight upload.
**Actor:** any user with active transfers, recipient of their transfers.
**Goal:** "apply at next finalize" is a rule with a name, enforced identically for plan switches and limit edits.

## Preconditions

- User has ≥ 1 active transfer and their plan changes (upgrade, downgrade, or limit-flag edit).

## Happy path

1. Plan change lands (F-BIL-002 webhook / admin plan edit / flag edit).
2. Active transfers keep their **send-time limits**: same retention, same download cap, same size allowance.
3. The next finalize resolves the new plan (F-BIL-003 middleware) — new transfer gets new limits.

## Alternative flows

- **Downgrade with over-quota transfers**: active transfers finish naturally; new sends blocked with the limit named (F-BIL-003-2, US-020-02).
- **Limit flag edited mid-flight**: same rule, 30 s cache (EC-007-1) — active transfers keep send-time values.

## Acceptance criteria

```gherkin
Given I have an active transfer on the pro plan
When my plan changes to free
Then the active transfer keeps its pro retention and download cap
And my next finalize uses the free limits

Given a limit flag is raised
Then new finalizes use the raised value
And in-flight transfers are unaffected
```

## Edge cases

- "Send-time limits" are the values resolved **at send/finalize**, not at page render — documented.
- Guest draft → user finalizes: draft keeps its creation-time validation (EC-020-4, documented).

## UI notes

- No UI on the moment of change; the effect is visible in My Files (retention/expiry unchanged for live rows) and at the next send.

## Technical notes

- Limits are resolved at `CreateDraftCommand` / `FinalizeTransferCommand` / `SendTransferCommand` (F-TRF-007 enforcement points) and stored where they matter (`ExpiresAtUtc`, `MaxDownloads`) — no re-resolution of live rows.
- `plan.changed` event (TA-5.3) marks the moment for audit/telemetry.

## Links

- Feature: `BIL-001-plan-catalog.md` (FR-018-4, AC-018-3)
- Related: F-BIL-003 (middleware), F-TRF-007 (EC-007-1), F-BIL-002 (webhook)
- Architecture: TA-5.3
