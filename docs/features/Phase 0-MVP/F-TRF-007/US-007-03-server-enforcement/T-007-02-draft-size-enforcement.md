# T-007-02 — Draft-time enforcement: transfer size + single file (FR-007-2)

**Story:** US-007-03 | **Spec:** FR-007-2, AC-007-1 (draft half), TA-4.1.3 | **Size:** M
**Depends on:** T-007-01 (Limits Registry — `ILimitsProvider.Resolve`), T-001-05 (CreateDraftCommand endpoint)

---

## Context to read (only these)

- `US-007-03-server-enforcement.md` → happy path + Alternative flows
- `../../F-TRF-007-limits.md` → FR-007-2/3 + AC-007-1 + Technical notes (enforcement map)

## Instructions

1. In **`CreateDraftCommand`**, resolve the caller's effective limits via `ILimitsProvider` (TA-3.4 — never literals): total of `files[].sizeBytes` > `MAX_TRANSFER_SIZE`, or any single file > `MAX_SINGLE_FILE`.
2. On violation return 400 Problem+JSON **`TRANSFER_SIZE_EXCEEDED`** with both numbers in `details` ("Transfer size 5.2 GB exceeds the 5 GB limit.").
3. Sizes are BIGINT bytes end-to-end (1024-based display only); the check is metadata-only — no blob reads, no re-upload.

## Exit check

- [ ] A raw API call creating a 5.2 GB draft on Free returns `TRANSFER_SIZE_EXCEEDED` with both numbers in details (AC-007-1)
- [ ] A single file over `MAX_SINGLE_FILE` is rejected even when the total fits
- [ ] No literals in the check path — all values come from the resolved record

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; CreateDraftCommand + Limits Registry in place).
Task T-007-02 — add draft-time size enforcement.
Read first (only): docs/features/Phase 0-MVP/F-TRF-007/US-007-03-server-enforcement/US-007-03-server-enforcement.md (happy path + Alternative flows) and F-TRF-007-limits.md (FR-007-2/3).
Do exactly:
1. In CreateDraftCommand, resolve the caller's effective limits via ILimitsProvider (TA-3.4 — never literals): total of files[].sizeBytes > MAX_TRANSFER_SIZE or any single file > MAX_SINGLE_FILE.
2. On violation return 400 Problem+JSON TRANSFER_SIZE_EXCEEDED with both numbers in details ("Transfer size 5.2 GB exceeds the 5 GB limit.").
3. Keep sizes as BIGINT bytes end-to-end (1024-based display only); the check is metadata-only — no blob reads, no re-upload.
Done when: AC-007-1's draft half holds — a raw API call with a 5.2 GB Free-plan draft gets TRANSFER_SIZE_EXCEEDED naming both numbers, and a single oversized file is caught even when the total fits.
Constraints: thin endpoint (TA-4.2a) — logic in CreateDraftCommand; error code from the closed TA-4.1.3 list only.
```
