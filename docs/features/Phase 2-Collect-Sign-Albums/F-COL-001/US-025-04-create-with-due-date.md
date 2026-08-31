# US-025-04 — Set a due date when creating

**Feature:** F-COL-001 — Create Collection | **Status:** pending

---

**Story:** As a collector, I want to set a due date on the collection, so that everyone knows when their files are due — and the past-due behavior (F-COL-004) starts from that date.
**Actor:** collector.
**Goal:** an optional date picker on the create screen; the date drives the `Open → PastDue → Closed` lifecycle.

## Preconditions

- Create-collection screen open (US-025-01).

## Happy path

1. Pick a due date (optional; leave empty for no deadline).
2. Stored as `DueAtUtc` (FR-025-1, TA-3.2).
3. F-COL-004 takes over: past-due banner, grace window, auto-close.

## Alternative flows

- **Due date in the past**: accepted, immediately `PastDue` (EC-025-4) — a "collecting late right now" case.
- **No due date**: `Open` forever (the normal case, F-COL-004-5).

## Acceptance criteria

```gherkin
Given I create a collection with a future due date
When the due date passes
Then the collection is PastDue (F-COL-004 banner shows)
```

## Edge cases

- Changing the due date after creation: allowed while Open/PastDue (the collection screen); re-Open also sets a new due date (F-COL-004-4).
- The due date is the single source — grace = due + `DUE_GRACE_DAYS` (F-COL-004).

## UI notes

- Date picker (native `<input type=date>`), optional; helper "Contributors can still submit during the grace window after this date."

## Technical notes

- `Collection.DueAtUtc` (TA-3.2); the timer sub-step (F-COL-004, TA-6.3) reads it.
- Index `(Status, DueAtUtc)` for the timer query (TA-3.2 addition).

## Links

- Feature: `COL-001-create-collection.md` (FR-025-1, AC-025-1)
- Related: F-COL-004 (lifecycle)
