# T-007-04 — Send-time enforcement: re-check + MAX_EMAILS cap + retention (AC-007-1)

**Story:** US-007-03 | **Spec:** FR-007-2/3, AC-007-1, TA-4.1.3 | **Size:** M
**Depends on:** T-007-01 (Limits Registry), T-002-04 (SendTransferCommand + endpoint 3)

---

## Context to read (only these)

- `US-007-03-server-enforcement.md` → happy path
- `../../F-TRF-007-limits.md` → FR-007-2/3 + AC-007-1 + Technical notes (enforcement map, RETENTION_DAYS)

## Instructions

1. In **`SendTransferCommand`**, re-check the transfer's total size against `MAX_TRANSFER_SIZE` via `ILimitsProvider` — a flag may have changed since draft/finalize; on violation → 400 Problem+JSON **`TRANSFER_SIZE_EXCEEDED`** naming both numbers (AC-007-1: "finalize succeeds but send fails").
2. Cap recipients at `MAX_EMAILS` (inclusive): 21 addresses on Free → cap error naming the limit.
3. Assign `ExpiresAtUtc = now + RETENTION_DAYS` from the resolved record — never a literal day count.

## Exit check

- [ ] A free user finalizing 5.2 GB gets accepted at finalize but rejected at send with "Transfer size 5.2 GB exceeds 5 GB limit" (AC-007-1)
- [ ] Sending to 21 recipients on Free is rejected with the MAX_EMAILS cap named; exactly 20 passes
- [ ] `ExpiresAtUtc` equals now + resolved RETENTION_DAYS for a freshly sent transfer

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; SendTransferCommand + Limits Registry in place).
Task T-007-04 — add send-time enforcement.
Read first (only): docs/features/Phase 0-MVP/F-TRF-007/US-007-03-server-enforcement/US-007-03-server-enforcement.md (happy path) and F-TRF-007-limits.md (FR-007-2/3 + AC-007-1).
Do exactly:
1. In SendTransferCommand, re-check the transfer's total size against MAX_TRANSFER_SIZE via ILimitsProvider — a flag may have changed since draft/finalize; on violation → 400 Problem+JSON TRANSFER_SIZE_EXCEEDED naming both numbers (AC-007-1: "finalize succeeds but send fails").
2. Cap recipients at MAX_EMAILS (inclusive): 21 addresses on Free → cap error naming the limit.
3. Assign ExpiresAtUtc = now + RETENTION_DAYS from the resolved record — never a literal day count.
Done when: AC-007-1 holds end-to-end — finalize of 5.2 GB succeeds, send fails with the exact message, and retention is set from the registry.
Constraints: caps are inclusive (exactly at the limit = allowed); error codes from the closed TA-4.1.3 list only; no literals in any check path.
```
