# US-005-02 — Expired storage is deleted after grace

**Feature:** F-TRF-005 — Expiry & Auto-Deletion | **Status:** pending

---

**Story:** As the operator, I want expired transfers to be physically deleted (rows + blobs) after the grace window, so that I pay storage only for live data — and re-send (F-TRF-010) still works inside the window.
**Actor:** Operator (system-level).
**Goal:** `Expired`/`DownloadLimit` → `Deleted` at `ExpiredAtUtc + GRACE_DAYS`; blobs vanish only when the last reference is gone.

## Preconditions

- GRACE_DAYS = 3 (all plans, Appendix A).
- `f-delete-transfers` (15-min timer) and `f-delete-blobs` (hourly) are running.

## Happy path

1. `f-delete-transfers` picks `Status IN (2,3) AND ExpiredAtUtc + GRACE_DAYS < now` (batch 100).
2. Per transfer, in a transaction: `Status=4` (`DeletedAtUtc`), `BlobRef.RefCount` decremented per `FileItem`, `FileItem` + `EmailRecipient` rows deleted, `transfers/{id}/all.zip` deleted, `transfer.deleted` emitted (reason `expiry-grace`).
3. `BlobRef` rows that hit `RefCount=0` are flagged `PhysicallyDeletedAtUtc = now + 24 h` (buffer).
4. `f-delete-blobs` (hourly) deletes those blobs + rows, swallowing `BlobNotFound` if lifecycle already swept them (EC-005-2).
5. My Files no longer shows the row; the recipient page (any of the states) has been gone or expired long before this point.

## Alternative flows

- **Re-send inside grace:** `BlobRef.RefCount > 0` → blob survives; the new transfer references it (US-010-03).
- **Lifecycle safety net fires first:** `transfers/*` > 30 d rule (TA-3.5) deletes a blob the job expected — `BlobNotFound` swallowed, row still deleted.

## Acceptance criteria

```gherkin
Given an expired transfer whose grace period has passed
When f-delete-transfers and f-delete-blobs run
Then the transfer row is Deleted and its FileItem and EmailRecipient rows are gone
And a transfer.deleted event with reason expiry-grace is emitted
And its blobs are physically deleted once no other transfer references them

Given the delete jobs run twice for the same window
When the second run processes the window
Then no double RefCount decrement, no duplicate events, no error
```

## Edge cases

- Download in flight during deletion: the 30-min SAS can outlive the row — documented, acceptable (EC-005-3).
- Shared blobs (re-send): refcount decides (EC-005-4); the 24 h physical buffer protects a concurrent re-send finalize.

## UI notes

- No direct user-facing UI (the states that *are* visible: My Files row gone; recipient page states are F-TRF-003).
- Admin: `active_transfers` / `storage_bytes_active` metrics on Overview (F-TRF-011).

## Technical notes

- Specs: TA-6.4 and TA-6.5 verbatim; claims via `JobRun` (TA-6.1); `transfer.deleted` payload `{transferId, reason}` (TA-5.3).
- Lifecycle rules remain the safety net only (FR-005-8).
- Metrics: `active_transfers`, `storage_bytes_active` (TA-10.2).

## Links

- Feature: `TRF-005-expiry-deletion.md` (FR-005-3, FR-005-6, FR-005-7, FR-005-8)
- Plan AC: AC-005-1 (second clause), AC-005-2
- Architecture: TA-6.4, TA-6.5, TA-3.5, TA-7.4
- Related: US-010-03 (shared blobs survive via refcount)
- Milestone: T-018
