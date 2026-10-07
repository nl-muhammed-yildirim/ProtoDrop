# T-009-01 — ListMyTransfersQuery + endpoint 14: owner-scoped list, cursor paging (FR-009-1/2)

**Story:** US-009-01 | **Spec:** FR-009-1/2, AC-009-1, TA-4.2#14/TA-4.1.4 | **Size:** M
**Depends on:** T-008-02 (session middleware — owner comes from the cookie), T-008-08 (send-time attribution — `OwnerAppUserId` populated), T-056 (Problem+JSON pipeline)

---

## Context to read (only these)

- `US-009-01-history-list.md` → happy path + Technical notes
- `../../F-TRF-009-my-files.md` → FR-009-1/2 + AC-009-1 + Technical notes (query shape, counts)

## Instructions

1. Add **`ListMyTransfersQuery`** (MediatR, `wa.application/UseCases/Transfers/`) + endpoint 14 `GET /api/v1/transfers?cursor=` (cookie required): `WHERE OwnerAppUserId=@me ORDER BY CreatedAtUtc DESC`, cursor-paginated — **25/page**, cursor format `base64(created_at:id)` (TA-4.1.4).
2. Each row returns: name (first file name or "Transfer"), `FileCount`, total size, recipient count (`COUNT(*)` subquery over `EmailRecipient` — cheap at MVP scale), status, `ExpiresAtUtc`, download count + cap (the client renders "87/100" from these).
3. Guests' transfers are never listed (no owner); deleted rows are already excluded (MVP: they disappear from the list).
4. Return `nextCursor` in the response metadata when another page exists; no `limit` parameter in MVP — fixed 25.

## Exit check

- [ ] User with 30 transfers → page 1 returns exactly 25 rows, most recent first (AC-009-1)
- [ ] Page 2 via the returned cursor → the remaining 5 rows; no duplicates or gaps across pages
- [ ] Guest-created transfer (`OwnerAppUserId=NULL`) is not listed for any signed-in user
- [ ] Row payload contains everything US-009-01's happy path renders — no extra round-trips per row

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; session middleware + send-time attribution in place).
Task T-009-01 — implement the My Files list endpoint (milestone task T-022, part 1).
Read first (only): docs/features/Phase 0-MVP/F-TRF-009/US-009-01-history-list/US-009-01-history-list.md (happy path + Technical notes) and F-TRF-009-my-files.md (FR-009-1/2).
Do exactly:
1. Add ListMyTransfersQuery (MediatR, wa.application/UseCases/Transfers/) + endpoint 14 GET /api/v1/transfers?cursor= (cookie required): WHERE OwnerAppUserId=@me ORDER BY CreatedAtUtc DESC, cursor-paginated — 25/page, cursor format base64(created_at:id) (TA-4.1.4).
2. Each row returns: name (first file name or "Transfer"), FileCount, total size, recipient count (COUNT(*) subquery over EmailRecipient), status, ExpiresAtUtc, download count + cap (client renders "87/100").
3. Guests' transfers are never listed (no owner); deleted rows already excluded — they disappear from the list in MVP.
4. Return nextCursor in the response metadata when another page exists; no limit parameter — fixed 25.
Done when: AC-009-1 holds server-side — 30 seeded transfers yield a first page of exactly 25 rows ordered by CreatedAtUtc desc, and cursor paging walks the rest without duplicates or gaps.
Constraints: thin endpoint (TA-4.2a); owner scope from the session cookie only — never trust a client-supplied ownerId; IX_Transfer_Owner serves the list (TA-3.3).
```
