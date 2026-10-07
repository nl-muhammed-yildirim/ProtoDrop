# T-009-04 — DeleteTransferCommand + endpoint 17: owner delete, refcount path (FR-009-3)

**Story:** US-009-04 | **Spec:** FR-009-3, AC-009-2 (server half), EC-009-1/2/3, TA-4.2#17/TA-5.3 | **Size:** M
**Depends on:** T-009-03 (endpoint 15 — owner-scoping rules + NOT_FOUND shape), T-054 (IEventPublisher + outbox)

---

## Context to read (only these)

- `US-009-04-delete.md` → happy path + Alternative flows + Technical notes
- `../../F-TRF-009-my-files.md` → FR-009-3 + AC-009-2 + EC-009-1/2/3 + Technical notes (refcount buffer)

## Instructions

1. Add **`DeleteTransferCommand`** (MediatR, `wa.application/UseCases/Transfers/`) + endpoint 17 `DELETE /api/v1/transfers/{id}` (cookie required): owner scope — the transfer must belong to the session user id (admin force-delete reuses this same command later with reason `admin`, US-011-02).
2. On success: mark the row **`Deleted`** (reason `user`) — not a hard delete; emit **`transfer.deleted`** (`{transferId, reason:"user"}`, TA-5.3); decrement each `BlobRef`'s `RefCount`; when a refcount hits 0 set **`PhysicallyDeletedAtUtc = now + 24h`** (same buffer as the expiry path — F-TRF-005 jobs finish the physical cleanup).
3. **Idempotent delete (EC-009-2):** second request for an already-deleted transfer → `NOT_FOUND`, which the client treats as success (no error toast) — no 409, no special code.
4. Storage quota frees immediately because `Status=Deleted` stops counting toward the F-TRF-007 meter; in-flight 30-min SAS URLs keep working (SAS outlives the row — documented, EC-009-1).

## Exit check

- [ ] Delete a transfer with 3 files → row marked Deleted (reason user), `transfer.deleted` emitted once, recipient page no longer serves files
- [ ] Second delete of the same id → `NOT_FOUND` (EC-009-2) — treated as success by the client
- [ ] BlobRef RefCounts decremented; a sole-reference blob gets `PhysicallyDeletedAtUtc = now + 24h`; shared blobs (RefCount > 1) stay untouched
- [ ] Guest's transfer or another user's id → `NOT_FOUND` (owner scope, no enumeration)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; session middleware + outbox in place).
Task T-009-04 — implement the owner delete endpoint (milestone task T-022, part 4).
Read first (only): docs/features/Phase 0-MVP/F-TRF-009/US-009-04-delete/US-009-04-delete.md (happy path + Alternative flows + Technical notes) and F-TRF-009-my-files.md (FR-009-3).
Do exactly:
1. Add DeleteTransferCommand (MediatR, wa.application/UseCases/Transfers/) + endpoint 17 DELETE /api/v1/transfers/{id} (cookie required): owner scope — the transfer must belong to the session user id (admin force-delete reuses this command later with reason admin).
2. On success: mark the row Deleted (reason user) — not a hard delete; emit transfer.deleted ({transferId, reason:"user"}, TA-5.3); decrement each BlobRef's RefCount; when a refcount hits 0 set PhysicallyDeletedAtUtc = now + 24h (same buffer as the expiry path).
3. Idempotent delete (EC-009-2): second request for an already-deleted transfer → NOT_FOUND, which the client treats as success — no 409, no special code.
4. Storage quota frees immediately because Status=Deleted stops counting toward the F-TRF-007 meter; in-flight 30-min SAS URLs keep working (SAS outlives the row — EC-009-1).
Done when: AC-009-2 holds server-side — delete marks the row Deleted with reason user, emits transfer.deleted once, drops refcounts (sole references get the 24 h buffer), and a double delete returns NOT_FOUND.
Constraints: same path as admin force-delete (one command, two reasons); "immediate" for the user = row gone + page dead — blobs follow the refcount + 24 h buffer path; delete is not subject to the grace window (owner intent > grace).
```
