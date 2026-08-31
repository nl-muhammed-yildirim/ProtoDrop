# US-028-03 — Have my collection close after the grace

**Feature:** F-COL-004 — Due Date & Grace | **Status:** pending

---

**Story:** As a collector, I want my collection to close automatically after the grace window, so that I don't have to remember to stop accepting — and I can re-open it if I need to.
**Actor:** collector, contributors after closure.
**Goal:** timer sets `Closed` at grace end; contributor page shows "This collection is closed."; re-open is one action.

## Preconditions

- A PastDue collection whose grace has ended.

## Happy path

1. Timer: `PastDue → Closed`, `ClosedAtUtc = now`.
2. Contributor page: "This collection is closed." (no drop zone).
3. Dashboard: gray `Closed {date}` chip + **Re-open** ghost button.

## Alternative flows

- **Re-open**: `Status = Open`, `ClosedAtUtc` cleared, optional new due date (AC-028-4).
- **In-flight submission at closure**: entry created then Declined `reason: closed` (F-COL-002 EC-026-2, consistent).

## Acceptance criteria

```gherkin
Given the grace window has ended
When the timer runs
Then the collection is Closed and new contributors see the closed state

When I re-open it
Then contributors can submit again and the banner is gone
```

## Edge cases

- Timer double-run → `JobRun` de-dup, one transition (AC-028-5).
- Closed collections are still **viewable** (entries + zip actions work) — closed ≠ deleted.
- Re-open is unlimited in count (documented, EC-028-5).

## UI notes

- Closed chip gray; **Re-open** ghost button; confirm modal only when no new due date is given.

## Technical notes

- Status machine: `Open → PastDue → Closed`, any → `Open` (re-open); timer sub-step (TA-6.3).
- `collection.status_changed` event per transition (TA-5.3 extension).

## Links

- Feature: `COL-004-due-date.md` (FR-028-3, FR-028-4, AC-028-3/4/5)
- Architecture: TA-6.1, TA-6.3
- Related: F-COL-001 (lifecycle owner)
