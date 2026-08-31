# F-PRF-002 — Scheduled Send

**Priority:** P1 | **Phase:** 1 — Pro
**Spec source:** `02-feature-plan.md` F-PRF-002 (outline FR-002-1…2 → remapped below as FR-022-*) | **Architecture:** TA-3.2 (`Transfer.ScheduledSendAtUtc`, `IX_Transfer_Sched`), TA-6.2, TA-5.3
**Milestone tasks:** Phase 1 backlog (not yet created)

---

## Description

Pro+ users can send **at a specific moment** instead of immediately: "Send at {datetime}". The link becomes live when the transfer is created, but the **emails go out on schedule** — and that behavior is documented on the link screen ("the link works early; emails go out on schedule"). A single 15-minute timer finds due transfers and hands them to the email worker exactly as an immediate send would.

**Actors:** Pro/Business user (schedules), recipient (gets the email at the scheduled time), operator (watches the schedule job's lag metric).
**Value:** time-zone-aware sending without a second product — the sender picks the moment, not the product.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-022-1 | Pro+: "Send at {datetime}" on the link screen. Emails go out at that time; the link is active from creation. Documented in the UI copy: "The link works early; emails go out on schedule." |
| FR-022-2 | Validation window: **now + 5 min** to **now + 14 days**. Outside the window → inline validation error naming the window (Problem+JSON `SCHEDULE_OUT_OF_WINDOW` on API, F-TRF-013 screen). |
| FR-022-3 | Gating: `PlanContext.Features.scheduling` (F-BIL-001); Free tier hides the control with a tooltip "Pro plan required" (plan screen deep-link, consistent with F-PRF-001). |
| FR-022-4 | A scheduled transfer stores `ScheduledSendAtUtc` (TA-3.2) and emits `transfer.created` **immediately** with `scheduledSendAt` in the payload — but the `f-email` consumer skips sending until the transfer's due time (status `Scheduled` → `Active` at due, TA-7.4 pattern). |
| FR-022-5 | The 15-minute timer (existing `f-expire` pass family, **no new function** — TA-6.2 inventory unchanged; sub-step in the same pass as expiry) flips `Scheduled → Active` and re-emits the email fan-out. Lag tolerance: emails land within one timer period of the due time. |
| FR-022-6 | Recipient page for a scheduled transfer before due: shows the file list with a "Files are ready — email goes out at {when}" line; downloads allowed (documented in FR-022-1). |
| FR-022-7 | Download cap, expiry, and deletion semantics are unchanged: `ExpiresAt` still from creation + `RETENTION_DAYS`; a scheduled transfer that expires before due is expired, emails suppressed (`transfer.expired` wins). |

## Acceptance criteria

```gherkin
AC-022-1: Pro user schedules a transfer for +2 hours
  Then the link is live now
  And no recipient email is sent before the due time
  And emails go out within one timer period of the due time

AC-022-2: User schedules for +2 min
  Then the form rejects with the 5-minute minimum named

AC-022-3: User schedules for +30 days
  Then the form rejects with the 14-day maximum named

AC-022-4: Free user opens the link screen
  Then the schedule control is present but disabled with "Pro plan required"

AC-022-5: A scheduled transfer expires before its due time
  Then it is expired, no emails are sent, and the page shows the expired screen

AC-022-6: The timer runs twice in the same period
  Then only one flip to Active (JobRun de-dup, TA-6.1)
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-022-1 | Due time passes while the API is down | Next timer run picks it up (due-time query is `WHERE Status=Scheduled AND due <= now`); no lost emails, lag documented |
| EC-022-2 | Recipient unsubscribes after scheduling, before due | Suppression check at actual send time (F-TRF-006-5 semantics) — skipped, not sent |
| EC-022-3 | Sender deletes a scheduled transfer | Normal delete; timer skips non-`Scheduled`/`Active` states (idempotent) |
| EC-022-4 | Scheduled send during email outage | `f-email` retries/DLQ exactly as F-TRF-006 (no special path) |
| EC-022-5 | User reschedules by editing the send | MVP: no edit — re-send flow (F-TRF-010) creates a new transfer with its own schedule (documented) |
| EC-022-6 | DST transition at due time | Due time stored UTC; UI displays in recipient-independent UTC offset of the sender's pick (documented, no per-recipient localizing in MVP) |

## UI notes (UI-Reference §5.2)

- Link screen: "Send now" / "Schedule" toggle; schedule picker (datetime-local) with helper "Between now and 14 days from now." Validation inline on submit.
- Confirmation screen: "Scheduled for {date, time} — the link is live, emails go out then."
- Recipient pre-due line: `--fs-small` `--fg-muted`, one line, no exclamation points.

## Technical notes

- Schema: `Transfer.ScheduledSendAtUtc` already in TA-3.2 DDL + `IX_Transfer_Sched` (hot query: `Status=Scheduled AND due <= now`).
- Status extension: `TransferStatus.Scheduled` (new TINYINT value 5 — **migration required**; domain enum change, ADR note). Order in DDL comments: Draft 0, Active 1, Expired 2, DownloadLimit 3, Deleted 4, Scheduled 5.
- Events: `transfer.created` payload already carries `scheduledSendAt?` (TA-5.3). Timer emits no new event type; the flip is a domain state change, and the email fan-out is the same `f-email` messages (TA-5.3 unchanged).
- Metrics: `schedule_lag_seconds` (TA-10.2 extension) + alert if p95 > one timer period.
- Timer: sub-step of the 15-minute expiry pass (TA-6.3) with its own `JobRun` claim key (TA-6.1 de-dup).

## Test plan

- Unit: window validation (5 min / 14 days, boundary values); status transition table (Scheduled → Active / Expired); due-time predicate.
- Integration: AC-022-1 (fake clock: no email before due, email after), AC-022-5, AC-022-6 (double-run de-dup); timer predicate hits `IX_Transfer_Sched` (query plan check).
- E2E: schedule +2 min with timer period overridden to 10 s (test config) → recipient email arrives; pre-due page line visible.

## User stories

| ID | Story | File |
|---|---|---|
| US-022-01 | Pick the moment my files arrive | `US-022-01-schedule-transfer.md` |
| US-022-02 | Know exactly what a scheduled send does | `US-022-02-schedule-semantics.md` |
| US-022-03 | Schedule within a sane window | `US-022-03-schedule-window.md` |
| US-022-04 | Get emails on time, even with a slow backend | `US-022-04-schedule-reliability.md` |
