# US-022-01 — Pick the moment my files arrive

**Feature:** F-PRF-002 — Scheduled Send | **Status:** pending

---

**Story:** As a Pro user, I want to schedule a transfer for a specific date and time, so that my recipients' emails arrive at the moment I choose — not when I happen to be online.
**Actor:** Pro/Business user, at the link screen.
**Goal:** "Send at {datetime}" on the link screen; emails go out on schedule, link live from creation.

## Preconditions

- User on Pro/Business (`PlanContext.Features.scheduling`).
- Files finalized, link screen open.

## Happy path

1. User picks "Schedule" and a datetime within the window (US-022-03).
2. `Transfer.ScheduledSendAtUtc` stored; `transfer.created` emitted immediately (with `scheduledSendAt` in the payload).
3. At due time the 15-minute timer flips `Scheduled → Active`; emails fan out (US-022-04).

## Alternative flows

- **Link opened before due**: file list + "Files are ready — email goes out at {when}" (FR-022-6); downloads allowed.
- **Free tier**: schedule control present but disabled ("Pro plan required").

## Acceptance criteria

```gherkin
Given a pro user schedules a transfer for +2 hours
When the link is opened now
Then it is live
And no emails are sent before the due time
And emails go out within one timer period of the due time
```

## Edge cases

- Expiry before due → expired, emails suppressed (AC-022-5, EC-022-3/7).
- Recipient unsubscribes before due → skipped at send time (EC-022-2).
- No reschedule in MVP — re-send creates a new transfer (EC-022-5, documented).

## UI notes

- Link screen: "Send now" / "Schedule" toggle + datetime picker with helper "Between now and 14 days from now."
- Confirmation: "Scheduled for {date, time} — the link is live, emails go out then."

## Technical notes

- `Transfer.ScheduledSendAtUtc` (TA-3.2, `IX_Transfer_Sched`); new `TransferStatus.Scheduled` (value 5, migration + ADR note).
- `transfer.created` payload carries `scheduledSendAt?` (TA-5.3); timer sub-step in the `f-expire` pass (TA-6.3, no new function).

## Links

- Feature: `PRF-002-scheduled-send.md` (FR-022-1, FR-022-4, AC-022-1)
- Architecture: TA-3.2, TA-5.3, TA-6.2, TA-6.3
- Related: F-BIL-001 (scheduling gate), F-TRF-006 (email fan-out)
