# F-COL-004 — Due Date & Grace

**Priority:** P1 (Phase 2a) | **Phase:** 2a — Collect
**Spec source:** `02-feature-plan.md` F-COL-004 (outline → remapped below as FR-028-*) | **Architecture:** TA-6.3, TA-6.1
**Milestone tasks:** T-045 (M5)

---

## Description

A collection with a **due date** says "I need these by {date}". Past due, the collector sees a **banner** and the contributor page shows the same state — but submissions are still accepted for a **grace window** (`DUE_GRACE_DAYS`, proposed 7, pending D-21). After the grace window the collection **auto-closes**: no more submissions, banner becomes "Closed". One 15-minute timer sub-step (inside the existing `f-expire` pass — no new function, TA-6.2 inventory unchanged) drives the `Open → PastDue → Closed` transitions.

**Actors:** collector (sees the banner), contributor (sees the state), operator.
**Value:** deadlines create urgency without a second product — the due date is a state, not a scheduler.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-028-1 | Past due: dashboard banner (amber, "Past due — accepting late submissions until {date}.") and contributor page line (same state, calmer: "This collection is past due — you can still submit until {date}.") |
| FR-028-2 | Grace: submissions are still accepted for `DUE_GRACE_DAYS` (proposed 7, pending D-21; constant in config, never a literal) after the due date. Late entries get a `late: true` flag in the dashboard (chip text "late"), no other special treatment. |
| FR-028-3 | Auto-close: after the grace window, the timer sets `Status = Closed`, `ClosedAtUtc = now`. Contributor page: "This collection is closed." (no drop zone); dashboard: gray "Closed {date}" chip. |
| FR-028-4 | Re-open: collector can re-open a closed collection (sets `Status = Open` again, clears `ClosedAtUtc`; new due date optional). Re-opens are finite in practice but unlimited in count (documented). |
| FR-028-5 | Collections without a due date stay `Open` forever (the normal case); the timer skips them. |
| FR-028-6 | Timer: sub-step of the 15-minute `f-expire` pass with its own `JobRun` claim key (TA-6.1 de-dup, same pattern as F-PRF-002's schedule sub-step). |

## Acceptance criteria

```gherkin
AC-028-1: A collection is past due
  Then the dashboard shows the past-due banner with the grace end date

AC-028-2: A submission arrives 3 days after the due date (grace = 7)
  Then the entry is accepted and flagged "late" in the dashboard

AC-028-3: The grace window ends
  Then the collection auto-closes and a new contributor sees "This collection is closed."

AC-028-4: The collector re-opens a closed collection
  Then contributors can submit again and the banner is gone

AC-028-5: The timer runs twice in one period
  Then only one transition occurs (JobRun de-dup)
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-028-1 | Due date with no grace confirmed yet (D-21 open) | Code reads the constant from config; launch value decided by D-21 (proposed 7 d) |
| EC-028-2 | API down when the grace ends | Next timer run closes it (due-time query is stateless) — same semantics as F-PRF-002 |
| EC-028-3 | Submission in flight when the collection closes | Finalize race: entry created, then immediately Declined `reason: closed` (F-COL-002 EC-026-2) — consistent |
| EC-028-4 | Collector sets a new due date on a PastDue collection | Extends grace accordingly (due date is the single source; grace = due + DUE_GRACE_DAYS) |
| EC-028-5 | Closed collection re-opened repeatedly | Each re-open is a `Status` change + `collection.status_changed` event; no versioning (documented) |

## UI notes (UI-Reference §5.4)

- Banner styles: amber past-due (dashboard + contributor, different copy per role), gray closed.
- Chip vocabulary (text always, F-TRF-016): `Open` (green) · `Past due` (amber) · `Closed` (gray).
- Re-open action: ghost button on the dashboard when closed; confirm modal ("Re-open this collection?") when no new due date is given (because it stays open).

## Technical notes

- Status transitions: `Open → PastDue` (due reached), `PastDue → Closed` (grace end), any → `Open` (re-open). Driven by the timer sub-step (TA-6.3), own `JobRun` key (TA-6.1).
- Events (TA-5.3 extension): `collection.status_changed { collectionId, from, to, reason }`.
- Telemetry (TA-10.2 extension): `collection_status_changed`.
- Query: `WHERE DueAtUtc IS NOT NULL AND Status=0 AND DueAtUtc <= now` / `Status=1 AND DueAtUtc + grace <= now` — index on `(Status, DueAtUtc)` (TA-3.2 addition).
- Late flag: computed at render (`CreatedAtUtc > DueAtUtc`) — no column needed (documented; avoids migration churn).

## Test plan

- Unit: state machine (all transitions, re-open); grace-date math (boundary: exactly due, exactly grace end); late flag computation.
- Integration: AC-028-1…028-5 with fake clock + 10 s timer override; double-run de-dup.
- E2E: create with due date → fake-clock to past-due → banner; late submission accepted; grace end → closed; re-open.

## User stories

| ID | Story | File |
|---|---|---|
| US-028-01 | See when my collection is past due | `US-028-01-past-due-banner.md` |
| US-028-02 | Accept late submissions during the grace window | `US-028-02-late-grace.md` |
| US-028-03 | Have my collection close after the grace | `US-028-03-auto-close.md` |
