# T-010-04 — Storage meter counts unique BlobRefs: no double-count on re-send (FR-010-1, AC-010-1)

**Story:** US-010-03 | **Spec:** FR-010-1 (storage math), AC-010-1 (blob-sharing clause), TA-3.2/TA-6.4/ADR-009 | **Size:** S
**Depends on:** T-010-01 (re-send creates the shared refs this task must not double-count), T-007-03/T-007-05 (the quota check + meter whose expressions get fixed here)

---

## Context to read (only these)

- `US-010-03-shared-blobs.md` → happy path + Edge cases + Technical notes
- `../../F-TRF-010-resend.md` → FR-010-1 + AC-010-1 + Technical notes (storage math line)

## Instructions

1. Fix the **per-user active-bytes expression** (T-007-03's quota check and T-007-05's meter share it — one query, reused everywhere): count **unique `BlobRef`s**, not transfer rows or `FileItem` rows — join `Transfer → FileItem → BlobRef`, dedupe by `BlobRefId`, sum `BlobRef.SizeBytes` where the transfer is active (`Status IN (1,3)`). A 2 GB transfer re-sent 10 times still counts as **2 GB**.
2. The same fix applies to the **admin storage meter / `storage_bytes_active` metric**: sum over `BlobRef`, not per `FileItem` (F-TRF-011-2 consumes it).
3. Keep the deletion path as-is — T-009-04's endpoint 17 already decrements RefCounts and sets the 24 h buffer on 0; this task only proves the round-trip holds: original deleted → `RefCount` 1 (blob survives); re-send deleted too → `RefCount` 0 → buffer set → blobs die via T-005-03.
4. Add the integration tests from US-010-03's ACs (see Exit check) — this is where "re-send costs 0 bytes" becomes a verified invariant, not an assumption.

## Exit check

- [ ] A 2 GB transfer re-sent → meter still shows 2 GB, not 4 GB (AC-010-1 blob-sharing clause); re-sent 10 times → still 2 GB
- [ ] Delete the original, keep the re-send → `RefCount` 1, blob survives; delete the re-send too → `RefCount` 0 + `PhysicallyDeletedAtUtc` buffer set (AC via US-010-03 gherkin)
- [ ] Quota check and meter use the **same** expression — an account at the limit can't send via re-send while the meter disagrees
- [ ] `storage_bytes_active` / admin storage total counts each physical blob once

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; ResendTransferCommand + endpoint 16, quota check T-007-03, meter T-007-05 in place).
Task T-010-04 — make the storage math count unique BlobRefs (milestone task T-023, part 4).
Read first (only): docs/features/Phase 0-MVP/F-TRF-010/US-010-03-shared-blobs/US-010-03-shared-blobs.md (happy path + Edge cases + Technical notes) and F-TRF-010-resend.md (FR-010-1).
Do exactly:
1. Fix the per-user active-bytes expression (T-007-03's quota check and T-007-05's meter share it — one query, reused everywhere): count unique BlobRefs, not transfer rows or FileItem rows — join Transfer → FileItem → BlobRef, dedupe by BlobRefId, sum BlobRef.SizeBytes where the transfer is active (Status IN (1,3)). A 2 GB transfer re-sent 10 times still counts as 2 GB.
2. The same fix applies to the admin storage meter / storage_bytes_active metric: sum over BlobRef, not per FileItem (F-TRF-011-2 consumes it).
3. Keep the deletion path as-is — T-009-04's endpoint 17 already decrements RefCounts and sets the 24 h buffer on 0; this task only proves the round-trip holds: original deleted → RefCount 1 (blob survives); re-send deleted too → RefCount 0 → buffer set → blobs die via T-005-03.
4. Add the integration tests from US-010-03's ACs — this is where "re-send costs 0 bytes" becomes a verified invariant, not an assumption.
Done when: AC-010-1's storage clause holds — re-sends never double-count in the quota check or any meter, and the delete/re-delete round-trip ends with the blob physically deleted after the buffer.
Constraints: one expression, reused by quota + user meter + admin metric (don't fork it); bytes are BIGINT end-to-end; don't touch T-005's jobs — they already honor refcount.
```
