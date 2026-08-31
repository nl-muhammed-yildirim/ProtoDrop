# US-028-02 — Accept late submissions during the grace window

**Feature:** F-COL-004 — Due Date & Grace | **Status:** pending

---

**Story:** As a collector, I want late submissions to be accepted during the grace window — flagged, not rejected — so that a slow sender doesn't get bounced by a deadline.
**Actor:** collector, late contributor.
**Goal:** entries arriving after the due date but before grace-end are accepted and flagged `late` in the dashboard.

## Preconditions

- A PastDue collection within its grace window.

## Happy path

1. Contributor submits after the due date (grace active).
2. Entry created normally (Status=Received).
3. Dashboard row shows a `late` flag (chip text, not color-only).

## Alternative flows

- **After grace end**: collection is Closed — the normal closed state applies (US-028-03).
- **Due date extended by the collector**: the grace end moves with the due date (EC-028-4).

## Acceptance criteria

```gherkin
Given my collection is past due with a 7-day grace
When a submission arrives 3 days after the due date
Then the entry is accepted and flagged "late"
```

## Edge cases

- Late flag is computed (`CreatedAtUtc > DueAtUtc`) — no column (F-COL-004 technical notes).
- A late submission doesn't reset the grace (the clock is the due date, not the last submission).

## UI notes

- Chip: `late` in `--fg-muted` `--fs-tiny` next to the row time — subtle, not alarming.

## Technical notes

- No schema change: the flag is render-time (F-COL-004).
- Entry otherwise identical to an on-time one (same status lifecycle, F-COL-005).

## Links

- Feature: `COL-004-due-date.md` (FR-028-2, AC-028-2)
- Related: F-COL-002 (submission), F-COL-003 (dashboard flag)
