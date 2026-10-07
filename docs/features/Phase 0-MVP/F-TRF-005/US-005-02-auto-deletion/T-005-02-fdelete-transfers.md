# T-005-02 — f-delete-transfers: grace scan, RefCount decrement, row/blob cleanup (TA-6.4)

**Story:** US-005-02 | **Spec:** FR-005-3/6/9, AC-005-1 (second clause), AC-005-2, TA-6.4 | **Size:** M
**Depends on:** T-005-01 (f-expire sets `Status=2` + `ExpiredAtUtc`)

---

## Context to read (only these)

- `US-005-02-auto-deletion.md` → happy path + Alternative flows + Edge cases
- `../../F-TRF-005-expiry-deletion.md` → FR-005-3/6/9 + AC-005-1…005-2 + Test plan (integration line)

## Instructions

1. Add **`f-delete-transfers`** to `wa.workers`: timer `0 */15 * * * *`, concurrency 1, 10-min timeout; claim via `JobRun` (`job=delete-transfers`).
2. Candidates: `Status IN (2,3) AND ExpiredAtUtc + GRACE_DAYS < @now` (**GRACE_DAYS = 3**, all plans); batch 100 until cursor exhausted (TA-6.4 verbatim).
3. Per transfer, in a transaction: `Status=4, DeletedAtUtc=@now`; decrement **`BlobRef.RefCount`** per `FileItem`; delete `FileItem` + `EmailRecipient` rows; delete the zip blob `transfers/{id}/all.zip` (swallow `BlobNotFound`, warn log); emit **`transfer.deleted`** with reason `expiry-grace`.
4. When a `BlobRef` hits `RefCount=0`: set **`PhysicallyDeletedAtUtc = now + 24 h`** — the buffer that protects a concurrent re-send finalize (EC-005-4).
5. Re-run-safe: a second run over the same window produces no double RefCount decrement, no duplicate events, no error (AC-005-2).

## Exit check

- [ ] Expired transfer past grace → row Status=4, FileItem + EmailRecipient rows gone, zip blob deleted (Azurite), `transfer.deleted` emitted with reason
- [ ] Shared blob (RefCount > 1) survives; only the last reference's grace triggers physical deletion eligibility
- [ ] Double run over the same window: no double decrement, no duplicate events

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.workers project, JobRun + f-expire in place).
Task T-005-02 — implement f-delete-transfers.
Read first (only): docs/features/Phase 0-MVP/F-TRF-005/US-005-02-auto-deletion/US-005-02-auto-deletion.md (happy path + Alternative flows + Edge cases) and F-TRF-005-expiry-deletion.md (FR-005-3/6/9).
Do exactly:
1. Add f-delete-transfers to wa.workers: timer 0 */15 * * * *, concurrency 1, 10-min timeout; claim via JobRun (job=delete-transfers).
2. Candidates: Status IN (2,3) AND ExpiredAtUtc + GRACE_DAYS < @now (GRACE_DAYS = 3, all plans); batch 100 until cursor exhausted (TA-6.4 verbatim).
3. Per transfer, in a transaction: Status=4, DeletedAtUtc=@now; decrement BlobRef.RefCount per FileItem; delete FileItem + EmailRecipient rows; delete the zip blob transfers/{id}/all.zip (swallow BlobNotFound, warn log); emit transfer.deleted with reason expiry-grace.
4. When a BlobRef hits RefCount=0: set PhysicallyDeletedAtUtc = now + 24 h — the buffer that protects a concurrent re-send finalize (EC-005-4).
5. Keep it re-run-safe: a second run over the same window produces no double RefCount decrement, no duplicate events, no error (AC-005-2).
Done when: AC-005-1's second clause holds — expired transfers past grace are fully cleaned with the event, shared blobs survive via refcount, and a double run is idempotent.
Constraints: GRACE_DAYS from Appendix A (never a literal in code); BlobNotFound swallowed as warn (EC-005-2); the 30-min SAS outliving a row during an in-flight download is documented-acceptable (EC-005-3) — do not add locks for it.
```
