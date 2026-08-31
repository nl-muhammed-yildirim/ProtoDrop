# US-029-01 — Mark a submission as done

**Feature:** F-COL-005 — Completion & Contributor Notification | **Status:** pending

---

**Story:** As a collector, I want to mark a submission Done, so that the list separates "finished with" from "still in front of me" — and so it's a terminal state I don't have to un-mark.
**Actor:** collector (owner).
**Goal:** **Done** is the terminal status: confirm modal, `DoneAtUtc`, contributor notified.

## Preconditions

- An entry with any non-Done status.

## Happy path

1. **Done** action → confirm modal: "Mark as done? The contributor will be notified."
2. Confirm → `Status = Done`, `DoneAtUtc = now`.
3. Contributor (with email) gets the "complete" email (US-029-02).

## Alternative flows

- **Declined/Accepted → Done**: both allowed (Done is the exit from any working state).
- **Done on a declined entry**: allowed (the decline is overridden by the collector's final word; documented).

## Acceptance criteria

```gherkin
Given a received or declined entry
When I mark it Done and confirm
Then the status is terminal and DoneAtUtc is set
```

## Edge cases

- Done is the only irreversible status (Accepted/Declined are reversible, F-COL-003).
- Done entry: zip action still available (the files are still there).

## UI notes

- Done rows: chip green, meta dimmed (opacity 0.75) — the text `Done` is the signal (F-TRF-016).
- Confirm modal text verbatim from the plan (one sentence, no exclamation points).

## Technical notes

- `SetEntryStatusCommand` transition rule: `* → Done` (any source); event `collection.entry.status_changed`.
- Telemetry `collection_status_changed` (TA-10.2 extension).

## Links

- Feature: `COL-005-completion.md` (FR-029-1, AC-029-1)
- Related: F-COL-003 (actions), F-COL-002 (entry)
