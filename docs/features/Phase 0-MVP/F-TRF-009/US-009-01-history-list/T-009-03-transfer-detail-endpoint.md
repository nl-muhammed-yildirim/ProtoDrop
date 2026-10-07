# T-009-03 — GetMyTransferQuery + endpoint 15: owner detail incl. recipient list (TA-4.2#15)

**Story:** US-009-01 | **Spec:** FR-009-1 (detail), TA-4.2#15 | **Size:** S
**Depends on:** T-009-01 (ListMyTransfersQuery — same owner-scoping rules + session middleware)

---

## Context to read (only these)

- `US-009-01-history-list.md` → happy path + Technical notes
- `../../F-TRF-009-my-files.md` → FR-009-1/3 + Technical notes (`GetMyTransferQuery`)

## Instructions

1. Add **`GetMyTransferQuery`** (MediatR, `wa.application/UseCases/Transfers/`) + endpoint 15 `GET /api/v1/transfers/{id}` (cookie required): return the transfer's detail — everything in a list row plus the per-file rows (name, size) and the **recipient list** (`EmailRecipient` addresses).
2. Owner scope: the transfer must belong to the session user id — someone else's transfer is `NOT_FOUND` (don't leak existence between accounts); guests' transfers are unreachable here too.
3. No row / deleted → 404 Problem+JSON **`NOT_FOUND`** (same body shape as a foreign owner's — no enumeration).

## Exit check

- [ ] Owner GETs one of their transfers → detail with file rows + recipient list, 200
- [ ] Another signed-in user GETs it → `NOT_FOUND` (no FORBIDDEN — don't leak which ids exist)
- [ ] Deleted or unknown id → `NOT_FOUND`, same body shape

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; ListMyTransfersQuery + session middleware in place).
Task T-009-03 — implement the My Files detail endpoint (milestone task T-022, part 3).
Read first (only): docs/features/Phase 0-MVP/F-TRF-009/US-009-01-history-list/US-009-01-history-list.md (happy path + Technical notes) and F-TRF-009-my-files.md (FR-009-1/3).
Do exactly:
1. Add GetMyTransferQuery (MediatR, wa.application/UseCases/Transfers/) + endpoint 15 GET /api/v1/transfers/{id} (cookie required): detail — everything in a list row plus per-file rows (name, size) and the recipient list (EmailRecipient addresses).
2. Owner scope: the transfer must belong to the session user id — someone else's transfer is NOT_FOUND (don't leak existence between accounts); guests' transfers unreachable here too.
3. No row / deleted → 404 Problem+JSON NOT_FOUND — same body shape as a foreign owner's (no enumeration).
Done when: an owner can fetch full detail (files + recipients) for any of their transfers, and every non-owner case returns the identical NOT_FOUND body.
Constraints: thin endpoint (TA-4.2a); owner scope from the session cookie only; no separate FORBIDDEN code — NOT_FOUND everywhere to keep enumeration cheap.
```
