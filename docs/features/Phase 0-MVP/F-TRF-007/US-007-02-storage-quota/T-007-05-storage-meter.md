# T-007-05 — Storage meter: per-user active bytes + storage_bytes_active metric (AC-007-2)

**Story:** US-007-02 | **Spec:** FR-007-4, AC-007-2 (meter half), TA-10.2 | **Size:** M
**Depends on:** T-007-03 (the quota check this meter reflects)

---

## Context to read (only these)

- `US-007-02-storage-quota.md` → happy path + UI notes
- `../../F-TRF-007-limits.md` → AC-007-2 + Technical notes (metric line)

## Instructions

1. Add the **per-user active-bytes query** used by the "Storage full" screen and (later, F-TRF-011) the admin storage meter: `SUM(TotalBytes) WHERE OwnerAppUserId=@me AND Status IN (1,3)` — same expression as T-007-03's check, so the meter and the law can't disagree.
2. Emit the **`storage_bytes_active`** metric (TA-10.2 template) on finalize/send so the admin overview storage meter has live data (F-TRF-011-2 consumes it).
3. Surface "Storage: X GB of Y GB" under the My Files header via the same query (UI notes — allowed in MVP).

## Exit check

- [ ] The per-user query returns exactly what T-007-03's quota check uses (same expression, same states)
- [ ] `storage_bytes_active` is emitted with the TA-10.2 template on finalize and send
- [ ] An account at 5 GB of 5 GB shows the meter at 100% (AC-007-2 second line)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; finalize/send enforcement in place).
Task T-007-05 — add the storage meter.
Read first (only): docs/features/Phase 0-MVP/F-TRF-007/US-007-02-storage-quota/US-007-02-storage-quota.md (happy path + UI notes) and F-TRF-007-limits.md (AC-007-2).
Do exactly:
1. Add the per-user active-bytes query used by the "Storage full" screen and the admin storage meter: SUM(TotalBytes) WHERE OwnerAppUserId=@me AND Status IN (1,3) — same expression as T-007-03's check so meter and law can't disagree.
2. Emit the storage_bytes_active metric (TA-10.2 template) on finalize and send so the admin overview storage meter has live data (F-TRF-011-2 consumes it).
3. Surface "Storage: X GB of Y GB" under the My Files header via the same query (UI notes — allowed in MVP).
Done when: AC-007-2's meter half holds — an account at 5 GB of 5 GB shows 100%, and the metric template matches TA-10.2 exactly.
Constraints: one query, reused everywhere — do not duplicate the expression; bytes are BIGINT end-to-end.
```
