# T-005-01 — f-expire: timer job, batch 500, JobRun claim (TA-6.3)

**Story:** US-005-01 | **Spec:** FR-005-1/2/5/9, AC-005-1 (first clause), AC-005-2, TA-6.3 | **Size:** M
**Depends on:** T-002-04 (send endpoint — `ExpiresAtUtc` set at send), T-052-03 (domain + WaDbContext)

---

## Context to read (only these)

- `US-005-01-auto-expiry.md` → happy path + Alternative flows + Edge cases
- `../../F-TRF-005-expiry-deletion.md` → FR-005-1/2/5/9 + AC-005-1…005-2 + Test plan (integration line)

## Instructions

1. Add **`f-expire`** to `wa.workers`: timer `0 */15 * * * *`, concurrency 1, 10-min timeout (TA-6.2).
2. Claim the job via **`JobRun`**: unique `(JobKey='expire', RunAtUtc=trunc(now,15m))` — a second instance or re-run in the same window no-ops (FR-005-9).
3. Scan `IX_Transfer_Expiry`: `Status=1 AND ExpiresAtUtc < @now`, **batches of 500**; per batch in a single transaction set `Status=2, ExpiredAtUtc=@now`; emit **`transfer.expired`** per row (TA-6.3 verbatim).
4. UTC only with a **±2 min tolerance window** against SQL/host clock skew — a transfer is never expired "early" by more than the skew margin (EC-005-1).
5. Note 5 of TA-6.3: if `DownloadsCount ≥ MaxDownloads`, set `Status=3` directly from `1` without waiting for expiry — that check lives in endpoint 6 (T-003-06), not here; the job just must not fight it.
6. Emit metric **`expiry_job_lag_seconds`** (TA-10.2); alert P1 at > 1800 s (TA-10.3).

## Exit check

- [ ] Seeded transfer with `ExpiresAtUtc` in the past → Status=2 + `ExpiredAtUtc` set + `transfer.expired` event
- [ ] Job run twice in the same window → no duplicate events, no error (AC-005-2)
- [ ] 10k expired rows complete within one 15-min window (< 10 min of it — load regression guard)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.workers project, JobRun table + IX_Transfer_Expiry in place).
Task T-005-01 — implement f-expire.
Read first (only): docs/features/Phase 0-MVP/F-TRF-005/US-005-01-auto-expiry/US-005-01-auto-expiry.md (happy path + Alternative flows + Edge cases) and F-TRF-005-expiry-deletion.md (FR-005-1/2/5/9).
Do exactly:
1. Add f-expire to wa.workers: timer 0 */15 * * * *, concurrency 1, 10-min timeout (TA-6.2).
2. Claim the job via JobRun: unique (JobKey='expire', RunAtUtc=trunc(now,15m)) — a second instance or re-run in the same window no-ops (FR-005-9).
3. Scan IX_Transfer_Expiry: Status=1 AND ExpiresAtUtc < @now, batches of 500; per batch in a single transaction set Status=2, ExpiredAtUtc=@now; emit transfer.expired per row (TA-6.3 verbatim).
4. Use UTC only with a ±2 min tolerance window against SQL/host clock skew — a transfer is never expired "early" by more than the skew margin (EC-005-1).
5. Keep TA-6.3 note 5 in mind: if DownloadsCount >= MaxDownloads, set Status=3 directly from 1 without waiting for expiry — that check lives in endpoint 6 (T-003-06), not here; the job just must not fight it.
6. Emit metric expiry_job_lag_seconds (TA-10.2); alert P1 at > 1800 s (TA-10.3).
Done when: AC-005-1's first clause and AC-005-2 hold — seeded past-expiry transfers flip to Status=2 with the event, a double run in one window is idempotent, and 10k rows clear inside one job window.
Constraints: batches of 500 in single transactions (TA-6.3); RETENTION_DAYS comes from the plan's LimitsRecord at send time — the job never recomputes it; cursor-exhaustion loop so a slow DB never starves the window.
```
