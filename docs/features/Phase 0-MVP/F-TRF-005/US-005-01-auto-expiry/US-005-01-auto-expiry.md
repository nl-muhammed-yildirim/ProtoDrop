# US-005-01 — Transfers expire automatically

**Feature:** F-TRF-005 — Expiry & Auto-Deletion | **Status:** pending

---

**Story:** As the operator, I want every transfer to expire on its own after the retention window, so that nothing lives on storage longer than its plan promises.
**Actor:** Operator (system-level); recipient sees the consequence (expired screen).
**Goal:** `Active` transfers become `Expired` within one job period after `ExpiresAtUtc`.

## Preconditions

- `f-expire` runs every 15 min (TA-6.2), claims its window via `JobRun` (TA-6.1).

## Happy path

1. A transfer is sent → `ExpiresAtUtc = send time + RETENTION_DAYS` (plan value).
2. After the deadline, `f-expire` finds it via `IX_Transfer_Expiry` (`Status=1 AND ExpiresAtUtc < now`).
3. In a batch transaction: `Status = Expired`, `ExpiredAtUtc = now`; `transfer.expired` emitted per row.
4. The recipient page now shows the expired screen (F-TRF-003-8): "This transfer has expired." + sender email if provided + **Send something**; no file list, no SAS minted.
5. The sender can still re-send inside the grace window (F-TRF-010).

## Alternative flows

- **Download cap first:** the transfer went `Active → DownloadLimit` (US-005-03) — it is picked up by deletion directly (TA-6.4 scans `Status IN (2,3)`).
- **Job down for an hour:** the next run catches up (lag > 30 min fires the P1 alert, TA-10.3).

## Acceptance criteria

```gherkin
Given an active transfer whose expiry passed 1 hour ago
When f-expire runs
Then the transfer is marked Expired with ExpiredAtUtc set
And a transfer.expired event is emitted
And the recipient page shows the expired screen without any file list

Given the expiry job runs twice in a row
When the second run processes the same window
Then no duplicate events are emitted and no error occurs
```

## Edge cases

- UTC only; ±2 min tolerance against SQL/host clock skew (EC-005-1).
- Batches of 500; cursor-exhaustion loop; a slow DB never starves the job window.

## UI notes

- Recipient side: F-TRF-003 expired screen (4.6-style single screen, "Send something" button).
- My Files: "Expired {date}" chip state (F-TRF-009).

## Technical notes

- Spec: TA-6.3 exactly (claim → batch → transaction → emit).
- `RETENTION_DAYS` from the plan's `LimitsRecord` (TA-3.4) — set at send time (F-TRF-002).
- Metric `expiry_job_lag_seconds` (TA-10.2); alert P1 at > 1800 s.

## Links

- Feature: `TRF-005-expiry-deletion.md` (FR-005-1, FR-005-2, FR-005-5, FR-005-9)
- Plan AC: AC-005-1 (first clause), AC-005-2
- Architecture: TA-6.3, TA-6.1, TA-7.4
- Milestone: T-018
