# F-TRF-005 — Expiry & Auto-Deletion

**Priority:** P0 (MVP) | **Phase:** 0 — Core Transfer
**Spec source:** `02-feature-plan.md` F-TRF-005 | **Architecture:** TA-6.1, TA-6.3–6.5, TA-7.4, TA-3.5
**Milestone tasks:** T-018

---

## Description

Storage is money. Every transfer dies twice: first it **expires** (recipient page switches to the expired screen, F-TRF-003-8), then — after a grace window that keeps re-send (F-TRF-010) possible — its rows and blobs are **physically deleted**. Both steps run as idempotent timer functions with a DB-claim de-dup (`JobRun`), so scale-out instances and re-runs never double-process. Blob Lifecycle Management is only a safety net; the jobs are primary.

**Actors:** operator (primary), recipient (sees the expired screen), sender (re-sends inside grace).
**Value:** storage bill proportional to actually-used transfers; clean data hygiene without a cleanup team.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-005-1 | Transfers expire `RETENTION_DAYS` after **send** (Free: 7; `ExpiresAtUtc` is set on send, F-TRF-002). Drafts (status 0) do not expire. *(The plan's "CreatedAt + RETENTION_DAYS" reading = creation of the active transfer; send-time is authoritative.)* |
| FR-005-2 | Expiration sets `Status = Expired`, `ExpiredAtUtc = now`; recipient page behavior is F-TRF-003-8 (dedicated expired screen, no file list, no SAS minted). |
| FR-005-3 | **Deletion** (blobs + rows) happens at `ExpiredAtUtc + GRACE_DAYS` (default 3) so a just-expired link can be revived by re-send (F-TRF-010). |
| FR-005-4 | Early end: if `DownloadsCount >= MaxDownloads`, the transfer goes straight to `Status = DownloadLimit` (set idempotently in the download-URL endpoint, TA-7.2 step 4); it is then cleaned up on the same deletion schedule. |
| FR-005-5 | `f-expire`: timer every 15 min; scans `IX_Transfer_Expiry` for `Status=1 AND ExpiresAtUtc < now`; batches of 500 in single transactions; emits `transfer.expired` per row. |
| FR-005-6 | `f-delete-transfers`: scans `Status IN (2,3) AND ExpiredAtUtc + GRACE_DAYS < now`; per transfer (transaction): `Status=4`, decrement `BlobRef.RefCount` per `FileItem`, delete `FileItem` + `EmailRecipient` rows, delete `transfers/{id}/all.zip`, emit `transfer.deleted` (reason `expiry-grace`). |
| FR-005-7 | `f-delete-blobs` (hourly): deletes `BlobRef` rows with `RefCount=0` and `PhysicallyDeletedAtUtc < now` (flagged with a 24 h buffer by `f-delete-transfers`), deletes the blob, removes the row. Skips paths not starting `transfers/` (log `UNEXPECTED_BLOB_PATH`). |
| FR-005-8 | Blob Lifecycle rules on `transfers/*` (30 d) and `staging/*` (24 h) are a **safety net only** (TA-3.5) — the jobs are primary. |
| FR-005-9 | All three jobs must be **idempotent**: single-row `JobRun` claim per 15-minute window (TA-6.1); re-runs produce no duplicate events, no double-decrement, no double-delete. |

## Acceptance criteria

```gherkin
AC-005-1: Transfer created now
  Then at expiry it is marked Expired within 15 min
  And at expiry+grace its blobs are deleted

AC-005-2: Expiry job runs twice in a row
  Then no duplicate events, no double-delete, no error

AC-005-3: Transfer hits download cap before time expiry
  Then it is DownloadLimit immediately (next page view), and the deletion jobs clean it up on schedule
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-005-1 | Clock skew between SQL and Function host | All timestamps UTC; jobs use a ±2 min tolerance window so a transfer is never expired "early" by more than a skew margin |
| EC-005-2 | Blob already deleted by lifecycle | Swallow `BlobNotFound` (warn log) — the row delete still proceeds |
| EC-005-3 | Deletion while a download is in flight | The recipient's 30-min SAS outlives the row (blob still there until refcount cleanup); documented, acceptable |
| EC-005-4 | `BlobRef` still referenced by another transfer (re-send share) | `RefCount` > 0 → blob **not** physically deleted until the last transfer's grace passes (F-TRF-010) |

## UI notes (UI-Reference §5.3)

- No dedicated "expiry screen" here — the recipient states are F-TRF-003's screens (expired: "This transfer has expired." + sender email if provided + **Send something**).
- My Files rows show "Expired {date}" instead of a countdown (F-TRF-009-3) — driven by `Status=2` + `ExpiredAtUtc`.
- Admin: job health (lag) visible on the Overview screen (F-TRF-011, US-011-01).

## Technical notes

- `f-expire` (TA-6.3): claim → batch 500 → `Status=2, ExpiredAtUtc=now` in one transaction per batch → `transfer.expired` per row → next batch until cursor exhausted.
- `f-delete-transfers` (TA-6.4): batch 100; sets `PhysicallyDeletedAtUtc = now + 24h` on affected `BlobRef`s *when* `RefCount` hits 0 (buffer before physical deletion).
- `f-delete-blobs` (TA-6.5): selects only `RefCount=0 AND PhysicallyDeletedAtUtc < now`; delete blob (swallow `BlobNotFound`), delete row.
- Job claims: `JobRun` unique `(JobKey, RunAtUtc=trunc(now,15m))` (TA-6.1); `f-delete-blobs` claims hourly.
- Events: `transfer.expired`, `transfer.deleted` (TA-5.3 payloads); metrics: `active_transfers`, `storage_bytes_active`, `expiry_job_lag_seconds` (TA-10.2).
- Alert: `expiry_job_lag_seconds > 1800` → P1 (TA-10.3).

## Test plan

- Unit: expiry math (`ExpiresAtUtc` from send + retention), grace math, `TransferStatus` transitions, tolerance window.
- Integration: f-expire on a seeded `ExpiresAtUtc`-in-past transfer → status 2 + event; run twice → idempotent; f-delete-transfers → rows gone, `RefCount` decremented, zip blob deleted (Azurite), `transfer.deleted` emitted with reason; f-delete-blobs → only refcount-0 blobs deleted.
- E2E: full lifecycle via admin flag override (`RETENTION_DAYS=0`): send → expired screen ≤15 min → deleted (My Files row gone after grace via flag override too).
- Load: 10k expired rows → job completes in one 15-min window (regression: < 10 min of that).

## User stories

| ID | Story | File |
|---|---|---|
| US-005-01 | Transfers expire automatically | `US-005-01-auto-expiry.md` |
| US-005-02 | Expired storage is deleted after grace | `US-005-02-auto-deletion.md` |
| US-005-03 | Download cap ends the transfer early | `US-005-03-download-limit.md` |
