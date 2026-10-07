# T-009-02 — Status filter on endpoint 14: All / Active / Expired (FR-009-4)

**Story:** US-009-02 | **Spec:** FR-009-4, TA-4.2#14/TA-4.1.4 | **Size:** S
**Depends on:** T-009-01 (ListMyTransfersQuery — this task adds the `?status=` predicate)

---

## Context to read (only these)

- `US-009-02-filters.md` → happy path + Technical notes (canonical status mapping)
- `../../F-TRF-009-my-files.md` → FR-009-4 + Technical notes (status chip mapping)

## Instructions

1. Extend **`ListMyTransfersQuery`** with an optional `?status=` filter: **`all`** (default — everything not physically gone), **`active`** = `Status=1`, **`expired`** = `Status IN (2,3)` (expired + download-limit — both are "no longer serving" states).
2. Filter and cursor compose: `?status=active&cursor=…` — the cursor encodes position within the filtered set; switching filters resets the client's cursor (UI side), server just applies both predicates.
3. Unknown status value → 400 Problem+JSON **`VALIDATION`** naming the allowed values.
4. No new index needed — `IX_Transfer_Owner` + the existing expiry index cover owner-scoped status predicates at MVP scale.

## Exit check

- [ ] 5 active + 4 expired transfers → `?status=active` returns exactly the 5; `?status=expired` returns the 4 (AC via US-009-02's gherkin)
- [ ] `?status=all` (or omitted) → all 9, most recent first
- [ ] Filter + cursor compose correctly — page 2 of `active` continues within the active set only
- [ ] Unknown status value → 400 VALIDATION naming allowed values

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; ListMyTransfersQuery + endpoint 14 in place).
Task T-009-02 — add the status filter to My Files (milestone task T-022, part 2).
Read first (only): docs/features/Phase 0-MVP/F-TRF-009/US-009-02-filters/US-009-02-filters.md (happy path + Technical notes — canonical status mapping) and F-TRF-009-my-files.md (FR-009-4).
Do exactly:
1. Extend ListMyTransfersQuery with an optional ?status= filter: all (default — everything not physically gone), active = Status=1, expired = Status IN (2,3) (expired + download-limit — both "no longer serving" states).
2. Filter and cursor compose (?status=active&cursor=…) — the server applies both predicates; the client resets its cursor when switching filters.
3. Unknown status value → 400 Problem+JSON VALIDATION naming the allowed values.
4. No new index — IX_Transfer_Owner + the existing expiry index cover owner-scoped status predicates at MVP scale.
Done when: US-009-02's ACs hold server-side — active returns only Status=1 rows, expired returns Status 2+3 (download-limit included), and all returns everything not physically gone.
Constraints: canonical mapping is the spec — don't invent new statuses; deleted rows stay out of every filter (MVP).
```
