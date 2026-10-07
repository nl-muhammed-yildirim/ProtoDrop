# T-010-02 — FILES_GONE predicate + draft cleanup (FR-010-4, AC-010-2)

**Story:** US-010-01 | **Spec:** FR-010-4, AC-010-2, EC-010-2, TA-4.1.3/TA-7.3 | **Size:** S
**Depends on:** T-010-01 (ResendTransferCommand — this task adds the blob-existence gate + rollback)

---

## Context to read (only these)

- `US-010-01-resend-click.md` → Alternative flows + Acceptance criteria
- `../../F-TRF-010-resend.md` → FR-010-4 + AC-010-2 + EC-010-2 + Technical notes (step 1)

## Instructions

1. Add the **blob-existence gate** as step 1 of `ResendTransferCommand`: verify every one of the transfer's `BlobRef`s still exists and is not physically deleted — **`PhysicallyDeletedAtUtc IS NULL`** and the blob head OK (container item present). If any fails → return **`FILES_GONE`** (TA-4.1.3) with no new rows written.
2. **Draft cleanup:** if `FILES_GONE` fires, roll back everything the re-send created (new `Transfer`, its `FileItem` rows, and the `RefCount` bumps already applied in the same transaction) — "no half-created draft remains" (AC-010-2). The gate + creation + bump all run inside one DB transaction.
3. **EC-010-2:** if the original transfer is deleted by its owner while a re-send draft is open, the finalize of that pending re-send fails with `FILES_GONE` — same predicate, applied at send time (the blob's refcount dropped to 0 and got the 24 h buffer). No orphan rows.
4. The client renders `FILES_GONE` as the single-screen state: **"Files were deleted — upload again."** + **Upload** primary (→ `/`) — that UI is T-010-03's scope; here just make sure the error code travels cleanly through the Problem+JSON pipeline.

## Exit check

- [ ] Re-send after `f-delete-blobs` ran on the original → 4xx `FILES_GONE`, zero new rows, no RefCount change (AC-010-2)
- [ ] Original deleted by owner while a re-send draft is open → finalize of the pending re-send fails with `FILES_GONE`; no orphan rows
- [ ] A transfer whose blob exists but has `PhysicallyDeletedAtUtc` set (buffer window, still physically present) → allowed (the gate checks physical deletion, not the buffer flag alone — verify against T-005-03's job semantics before asserting either way)
- [ ] The error body is Problem+JSON with code `FILES_GONE` — no stack traces

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; ResendTransferCommand + endpoint 16 in place).
Task T-010-02 — add the FILES_GONE gate + draft cleanup (milestone task T-023, part 2).
Read first (only): docs/features/Phase 0-MVP/F-TRF-010/US-010-01-resend-click/US-010-01-resend-click.md (Alternative flows + Acceptance criteria) and F-TRF-010-resend.md (FR-010-4).
Do exactly:
1. Add the blob-existence gate as step 1 of ResendTransferCommand: verify every one of the transfer's BlobRefs still exists and is not physically deleted — PhysicallyDeletedAtUtc IS NULL and the blob head OK (container item present). If any fails → return FILES_GONE (TA-4.1.3) with no new rows written.
2. Draft cleanup: if FILES_GONE fires, roll back everything the re-send created (new Transfer, its FileItem rows, and the RefCount bumps already applied in the same transaction) — "no half-created draft remains" (AC-010-2). Gate + creation + bump all run inside one DB transaction.
3. EC-010-2: if the original transfer is deleted by its owner while a re-send draft is open, the finalize of that pending re-send fails with FILES_GONE — same predicate, applied at send time (the blob's refcount dropped to 0 and got the 24 h buffer). No orphan rows.
4. The client renders FILES_GONE as the single-screen state "Files were deleted — upload again." + Upload primary (→ /) — that UI is T-010-03's scope; here just make sure the error code travels cleanly through the Problem+JSON pipeline.
Done when: AC-010-2 holds server-side — a re-send against physically-deleted blobs returns FILES_GONE with zero new rows, and a draft finalized after the original was deleted fails the same way without orphans.
Constraints: one predicate shared between create-time and send-time (don't duplicate it); the 24 h buffer is T-005-03's job — this task only reads its output.
```
