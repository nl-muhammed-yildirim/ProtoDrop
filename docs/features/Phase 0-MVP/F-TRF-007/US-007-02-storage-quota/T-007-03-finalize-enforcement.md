# T-007-03 — Finalize-time enforcement: storage quota, active transfers, blob size truth (FR-007-4)

**Story:** US-007-02 | **Spec:** FR-007-4/2, AC-007-2/3, EC-007-2/3 | **Size:** M
**Depends on:** T-007-01 (Limits Registry), T-001-10 (FinalizeTransferCommand + blob copy)

---

## Context to read (only these)

- `US-007-02-storage-quota.md` → happy path + Alternative flows + Technical notes
- `US-007-03-server-enforcement.md` → Alternative flows (client lied about sizes)
- `../../F-TRF-007-limits.md` → FR-007-4 + AC-007-2/3 + EC-007-2/3 + Technical notes (enforcement map)

## Instructions

1. In **`FinalizeTransferCommand`**, compute the account's active storage: `SUM(TotalBytes) WHERE OwnerAppUserId=@me AND Status IN (1,3)` (Active + DownloadLimit only). If this transfer would push it over `STORAGE_QUOTA_FREE` → 400 Problem+JSON **`STORAGE_QUOTA_EXCEEDED`** ("Storage full — X GB of Y GB in use.").
2. Count the account's active transfers; at or over `ACTIVE_TRANSFERS_MAX_FREE` → reject with the limit named ("You have 20 active transfers (limit 20).").
3. Verify each blob's real size against the declared `sizeBytes` (±0 tolerance) — the blob `Properties.Length` is ground truth; mismatch → 400 **`VALIDATION`** naming the file (the draft was metadata-only, so this catches a lying client).
4. Guests (no owner) skip both account checks — per-transfer limits only (EC-007-3).

## Exit check

- [ ] Finalize of a 200 MB transfer against a 4.9 GB active total (5 GB quota) is rejected with "Storage full" showing the numbers (AC-007-2)
- [ ] A 21st active transfer on Free is rejected with the limit named (AC-007-3)
- [ ] A file declared 100 MB but actually 300 MB in the blob is caught at finalize and the response names the file

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; FinalizeTransferCommand + Limits Registry in place).
Task T-007-03 — add finalize-time enforcement.
Read first (only): docs/features/Phase 0-MVP/F-TRF-007/US-007-02-storage-quota/US-007-02-storage-quota.md (happy path + Alternative flows), US-007-03-server-enforcement.md (Alternative flows — client lied about sizes), and F-TRF-007-limits.md (FR-007-4).
Do exactly:
1. In FinalizeTransferCommand, compute the account's active storage SUM(TotalBytes) WHERE OwnerAppUserId=@me AND Status IN (1,3) — Active + DownloadLimit only; if this transfer pushes it over STORAGE_QUOTA_FREE return 400 Problem+JSON STORAGE_QUOTA_EXCEEDED ("Storage full — X GB of Y GB in use.").
2. Count the account's active transfers; at or over ACTIVE_TRANSFERS_MAX_FREE reject with the limit named ("You have 20 active transfers (limit 20).").
3. Verify each blob's real size against the declared sizeBytes (±0 tolerance) — blob Properties.Length is ground truth; mismatch → 400 VALIDATION naming the file.
4. Guests (no owner) skip both account checks — per-transfer limits only (EC-007-3).
Done when: AC-007-2 and AC-007-3 hold, and a lying client's blob is caught at finalize with the file named in the response.
Constraints: quota read is transactional but not row-locked — concurrent sends may race by one transfer (EC-007-2, documented); re-send must not double-count shared bytes (US-010-3 builds on BlobRef RefCount later).
```
