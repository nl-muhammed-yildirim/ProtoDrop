# T-010-01 — ResendTransferCommand + endpoint 16: new draft sharing blobs (FR-010-1/2/3)

**Story:** US-010-01 | **Spec:** FR-010-1/2/3, AC-010-1, TA-4.2#16/TA-7.3/ADR-009 | **Size:** M
**Depends on:** T-001-10 (finalize — `BlobRef` + `FileItem` junction in place day one), T-002-04 (`SendTransferCommand` — the re-send draft is sent through it), T-008-02 (session middleware — owner from cookie), T-054 (IEventPublisher + outbox)

---

## Context to read (only these)

- `US-010-01-resend-click.md` → happy path + Technical notes
- `../../F-TRF-010-resend.md` → FR-010-1/2/3 + AC-010-1 + Technical notes (the 4-step command)

## Instructions

1. Add **`ResendTransferCommand`** (MediatR, `wa.application/UseCases/Transfers/`) + endpoint 16 `POST /api/v1/transfers/{id}/resend` (cookie required): owner scope — the transfer must belong to the session user id; guests' transfers are unreachable (EC-010-4).
2. Create a **new** `Transfer(Status=0, ExpiresAtUtc = now + RETENTION_DAYS, DownloadsCount=0)` with new `FileItem` rows pointing at the **same `BlobRefId`s**, and bump each shared blob's **`RefCount++`** (no data copy — ADR-009). The new link is a fresh 8-char Crockford (F-TRF-002) — no link reuse.
3. **Pre-fill as a snapshot copy** of the original at re-send time: `EmailRecipient` addresses, `PasswordHash`, `Note`, `SenderName`/`SenderEmail`. These are materialized on the new draft's pending state (editable before send — US-010-02); editing never mutates the original.
4. The new draft row records its **source** (the original transfer id) so that when `SendTransferCommand` activates it, **`SupersededBy = newTransferId`** is set on the original (FR-010-3). Extend T-002-04's send path minimally: if the draft carries a source reference, set `SupersededBy` in the same transaction as the status flip.
5. Re-send of an **active** transfer is fully allowed (two live links, same files); re-send of a re-send works — the chain stays linear (`RefCount` 2 → 3).

## Exit check

- [ ] Re-send an expired transfer within grace → new `Transfer` with a new linkId, same `BlobRefId`s, each shared blob's `RefCount` = 2, original `Status` unchanged (AC-010-1)
- [ ] Pre-filled emails/password/note/sender are present on the draft and **editable** — editing them does not touch the original's rows
- [ ] After send: original gains `SupersededBy = newTransferId`; both links work simultaneously; `transfer.created` emitted for the new transfer (normal path)
- [ ] Re-send of a re-send → `RefCount` 3, linear chain (no cycles); a guest's transfer (no owner) → `FORBIDDEN` (EC-010-4); another user's id → `NOT_FOUND`

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; finalize + BlobRef/FileItem junction, SendTransferCommand, session middleware, outbox in place).
Task T-010-01 — implement the re-send command (milestone task T-023, part 1).
Read first (only): docs/features/Phase 0-MVP/F-TRF-010/US-010-01-resend-click/US-010-01-resend-click.md (happy path + Technical notes) and F-TRF-010-resend.md (FR-010-1/2/3).
Do exactly:
1. Add ResendTransferCommand (MediatR, wa.application/UseCases/Transfers/) + endpoint 16 POST /api/v1/transfers/{id}/resend (cookie required): owner scope — the transfer must belong to the session user id; guests' transfers unreachable (EC-010-4).
2. Create a new Transfer(Status=0, ExpiresAtUtc = now + RETENTION_DAYS, DownloadsCount=0) with new FileItem rows pointing at the same BlobRefIds, and bump each shared blob's RefCount++ (no data copy — ADR-009). New link is a fresh 8-char Crockford — no link reuse.
3. Pre-fill as a snapshot copy of the original at re-send time: EmailRecipient addresses, PasswordHash, Note, SenderName/SenderEmail — materialized on the new draft's pending state (editable before send); editing never mutates the original.
4. The new draft row records its source (original transfer id) so that when SendTransferCommand activates it, SupersededBy = newTransferId is set on the original (FR-010-3). Extend T-002-04's send path minimally: if the draft carries a source reference, set SupersededBy in the same transaction as the status flip.
5. Re-send of an active transfer is fully allowed (two live links); re-send of a re-send works — chain stays linear (RefCount 2 → 3).
Done when: AC-010-1 holds server-side — new linkId, same BlobRefIds with RefCount 2, original intact and still downloadable, SupersededBy set on send.
Constraints: thin endpoint (TA-4.2a); owner scope from the session cookie only; guest's transfer → FORBIDDEN (EC-010-4), foreign owner → NOT_FOUND (no enumeration); no data copy — re-send costs 0 bytes (ADR-009); the FILES_GONE predicate + draft cleanup are T-010-02's scope, don't build them here.
```
