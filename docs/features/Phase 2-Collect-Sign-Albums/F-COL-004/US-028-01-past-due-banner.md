# US-028-01 — See when my collection is past due

**Feature:** F-COL-004 — Due Date & Grace | **Status:** pending

---

**Story:** As a collector with a due date, I want a clear past-due state on my dashboard — and the same state on the contributor page — so that everyone knows the deadline has passed.
**Actor:** collector, contributors.
**Goal:** amber past-due banner: dashboard "Past due — accepting late submissions until {date}."; contributor page "This collection is past due — you can still submit until {date}."

## Preconditions

- A collection with a due date that has passed (and grace not yet ended).

## Happy path

1. Timer flips `Open → PastDue` (due reached, F-COL-004-6).
2. Dashboard banner (amber) with the grace-end date.
3. Contributor page shows the calmer version of the same state — the drop zone is **still active**.

## Alternative flows

- **No due date**: no banner, ever (FR-028-5).
- **Re-opened collection**: banner gone, `Open` chip.

## Acceptance criteria

```gherkin
Given my collection is past due
When I open the dashboard
Then the past-due banner shows with the grace end date

When a contributor opens the link
Then the past-due line is shown and the drop zone is active
```

## Edge cases

- Grace-end date = due + `DUE_GRACE_DAYS` (config constant, D-21).
- The two copies of the state (dashboard / contributor) share the same data, different tone (F-TRF-016: both state the date).

## UI notes

- Banner: `--warning` left border, `--bg-elevated` bg, date in `--fs-small`.
- Chip: `Past due` (amber, text present).

## Technical notes

- `collection.status_changed { collectionId, from, to, reason }` (TA-5.3 extension) on the transition.
- Timer sub-step in the `f-expire` pass, own `JobRun` key (TA-6.1/6.3, no new function).

## Links

- Feature: `COL-004-due-date.md` (FR-028-1, AC-028-1)
- Architecture: TA-6.1, TA-6.3
- Related: F-COL-001 (due date), F-COL-002 (contributor page)
