# US-027-03 — Accept, decline, or complete a submission

**Feature:** F-COL-003 — Submissions Dashboard | **Status:** pending

---

**Story:** As a collector, I want to mark a submission accepted, declined, or done, so that the dashboard tells me at a glance what still needs my attention.
**Actor:** collector (owner).
**Goal:** status actions per entry: **Accepted** (reversible), **Declined** (reversible until Done), **Done** (terminal, F-COL-005).

## Preconditions

- An entry with `Received` (or an already-changed) status.

## Happy path

1. **Accepted** → chip turns blue; contributor notified (F-COL-005).
2. **Declined** → chip amber; contributor gets the "resubmit" email.
3. **Done** → confirm modal ("Mark as done? The contributor will be notified.") → green chip, terminal.

## Alternative flows

- **Re-accept a declined entry**: allowed until Done (EC-027-3).
- **Decline with no modal**: Decline/Accept are reversible → no confirm; only Done confirms.

## Acceptance criteria

```gherkin
Given a received entry
When I mark it Declined
Then the chip updates and the contributor is notified

When I mark it Done
Then the confirm modal appears and, on confirm, the status is terminal
```

## Edge cases

- Done on an entry with a bounced address: state stands, delivery is decoupled (F-COL-005 EC-029-2).
- Events: `collection.entry.status_changed { collectionId, entryId, from, to }` (TA-5.3 extension).

## UI notes

- Chip vocabulary (text + color, F-TRF-016): `Received` / `Accepted` / `Declined` / `Done`.
- Confirm modal only for **Done** (the only irreversible action).

## Technical notes

- `PATCH /collections/{id}/entries/{entryId}` (endpoint 31); `SetEntryStatusCommand` (F-COL-005 owns the email fan-out).
- Telemetry `collection_status_changed` (TA-10.2 extension).

## Links

- Feature: `COL-003-submissions-dashboard.md` (FR-027-3, AC-027-4)
- Related: F-COL-005 (notifications), F-COL-002 (entry)
