# T-005-03 — f-delete-blobs: hourly physical deletion of refcount-0 blobs (TA-6.5)

**Story:** US-005-02 | **Spec:** FR-005-7/8, AC-005-1 (second clause), TA-6.5 | **Size:** S
**Depends on:** T-005-02 (f-delete-transfers flags `PhysicallyDeletedAtUtc`)

---

## Context to read (only these)

- `US-005-02-auto-deletion.md` → happy path + Alternative flows + Edge cases
- `../../F-TRF-005-expiry-deletion.md` → FR-005-7/8 + EC-005-2/4 + Test plan (integration line)

## Instructions

1. Add **`f-delete-blobs`** to `wa.workers`: timer `0 0 * * * *` (hourly), concurrency 1, 30-min timeout; claim via `JobRun` (`job=delete-blobs`, hourly window).
2. Select only: `RefCount=0 AND PhysicallyDeletedAtUtc < @now` (the 24 h buffer set by f-delete-transfers) — delete the blob, then remove the row (TA-6.5 verbatim).
3. Safety: skip paths not starting with **`transfers/`** — log `UNEXPECTED_BLOB_PATH` (never touch staging or other prefixes).
4. Swallow **`BlobNotFound`** (warn log) when the lifecycle safety net already swept the blob — the row delete still proceeds (EC-005-2).
5. Keep Blob Lifecycle rules on `transfers/*` (30 d) and `staging/*` (24 h) as **safety net only** (FR-005-8, TA-3.5) — do not rely on them for correctness.

## Exit check

- [ ] Only refcount-0 blobs past their 24 h buffer are deleted; referenced blobs survive
- [ ] A blob already gone via lifecycle → row still deleted (BlobNotFound swallowed, warn logged)
- [ ] Non-`transfers/` path is skipped with `UNEXPECTED_BLOB_PATH` log

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.workers project, f-delete-transfers in place).
Task T-005-03 — implement f-delete-blobs.
Read first (only): docs/features/Phase 0-MVP/F-TRF-005/US-005-02-auto-deletion/US-005-02-auto-deletion.md (happy path + Alternative flows + Edge cases) and F-TRF-005-expiry-deletion.md (FR-005-7/8).
Do exactly:
1. Add f-delete-blobs to wa.workers: timer 0 0 * * * * (hourly), concurrency 1, 30-min timeout; claim via JobRun (job=delete-blobs, hourly window).
2. Select only RefCount=0 AND PhysicallyDeletedAtUtc < @now (the 24 h buffer set by f-delete-transfers) — delete the blob, then remove the row (TA-6.5 verbatim).
3. Safety: skip paths not starting with transfers/ — log UNEXPECTED_BLOB_PATH (never touch staging or other prefixes).
4. Swallow BlobNotFound (warn log) when the lifecycle safety net already swept the blob — the row delete still proceeds (EC-005-2).
5. Keep Blob Lifecycle rules on transfers/* (30 d) and staging/* (24 h) as safety net only (FR-005-8, TA-3.5) — do not rely on them for correctness.
Done when: the integration tests hold — only refcount-0 blobs past their buffer are deleted, referenced blobs survive, BlobNotFound is swallowed with the row still removed, and unexpected paths are skipped with a log.
Constraints: hourly JobRun claim (TA-6.1); no new metrics required beyond existing storage_bytes_active movement; this job never decrements RefCount — that was f-delete-transfers' job.
```
