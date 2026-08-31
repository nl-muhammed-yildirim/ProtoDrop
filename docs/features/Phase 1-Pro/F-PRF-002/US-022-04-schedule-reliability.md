# US-022-04 — Get emails on time, even with a slow backend

**Feature:** F-PRF-002 — Scheduled Send | **Status:** pending

---

**Story:** As a Pro user, I want scheduled emails to land within one timer period of the due time — even if the API is briefly down — so that "on schedule" is a promise I can give my recipients.
**Actor:** Pro/Business user, their recipients, operator (watching lag).
**Goal:** due-time query `Status=Scheduled AND due <= now` on the 15-minute pass; de-dup; lag measured and alerted.

## Preconditions

- Scheduled transfer stored; timer pass running (TA-6.2, 15 min).

## Happy path

1. Due time passes.
2. Next timer run picks it up → `Scheduled → Active` → email fan-out (same `f-email` messages as immediate send).
3. Emails land within one timer period of the due time.

## Alternative flows

- **API down at due**: next run catches it (due-time query is stateless, EC-022-1).
- **Timer runs twice in a period**: `JobRun` de-dup → one flip (TA-6.1, AC-022-6).
- **Email outage at due**: `f-email` retries/DLQ (F-TRF-006 semantics).

## Acceptance criteria

```gherkin
Given a scheduled transfer whose due time has passed
When the timer runs
Then it is flipped to Active and emails fan out
And a second run in the same period does not re-flip

Given the lag metric
When p95 exceeds one timer period
Then an alert fires
```

## Edge cases

- Suppression at actual send time (EC-022-2); expiry wins (EC-022-7/AC-022-5).
- Lag tolerance is documented as "within one timer period" — never "exact second."

## UI notes

- Invisible to users; the confirmation screen's time is the contract.

## Technical notes

- Timer sub-step of the `f-expire` pass (TA-6.3), own `JobRun` claim key (TA-6.1).
- Metric `schedule_lag_seconds` (TA-10.2 extension) + p95 alert (TA-10.3).
- Query hits `IX_Transfer_Sched` (TA-3.2 hot index).

## Links

- Feature: `PRF-002-scheduled-send.md` (FR-022-4, FR-022-5, AC-022-1/6)
- Architecture: TA-6.1, TA-6.2, TA-6.3, TA-10.2, TA-3.2
- Related: F-TRF-006 (email pipeline)
