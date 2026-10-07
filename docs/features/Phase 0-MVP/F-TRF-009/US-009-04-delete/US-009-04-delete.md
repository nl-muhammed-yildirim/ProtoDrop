# US-009-04 — Delete one of my transfers

**Feature:** F-TRF-009 — My Files (History) | **Status:** pending

---

**Story:** As a user, I want to delete a specific transfer — with a confirm that tells me exactly what's going away — so that I can free storage or kill a link I shared too widely.
**Actor:** Signed-in owner of the transfer.
**Goal:** Delete with eyes open: the confirm names the file count and size.

## Preconditions

- The transfer is in the list (any status except physically deleted).

## Happy path

1. User presses the trash action on a row.
2. Confirm modal: "Delete transfer? 3 files, 1.2 GB. This can't be undone." (**names count + size** — AC-009-2).
3. **Delete** → `DELETE /transfers/{id}`: row marked `Deleted` (reason `user`), `transfer.deleted` emitted, recipient page dies (404-vs-410 indistinguishable, F-TRF-003-9).
4. The row disappears from the list; storage quota frees (F-TRF-007-4); `BlobRef` refcounts drop (F-TRF-005 jobs finish the physical work).

## Alternative flows

- **Second tab deletes the same transfer:** the other tab's delete gets `NOT_FOUND` → treated as success (row quietly vanishes, no error toast, EC-009-2).
- **Delete an active transfer with pending downloads:** in-flight 30-min SAS may still complete — documented (EC-009-1).

## Acceptance criteria

```gherkin
Given I press delete on a transfer with 3 files totaling 1.2 GB
When the confirm modal appears
Then it says "Delete transfer? 3 files, 1.2 GB."
When I press Delete
Then the row is gone, transfer.deleted is emitted with reason user, and the recipient page no longer serves files

Given two tabs both delete the same transfer
When the second delete lands
Then it returns NOT_FOUND and is treated as success (no error toast)
```

## Edge cases

- "Immediate" for the user = row gone + page dead; blobs follow the refcount + 24 h buffer path (F-TRF-005) — the meter updates promptly because `Status=Deleted` stops counting toward quota.
- Delete is **not** subject to the grace window (owner intent > grace, contrast with F-TRF-005-3).

## UI notes

- Trash icon (ghost) in row actions; confirm modal per UI-Reference §4.5 (danger button).
- Toast after delete: "Transfer deleted." (success, `role=status`).

## Technical notes

- `DeleteTransferCommand` (endpoint 17): same path as admin force-delete with reason `user`; refcount + `PhysicallyDeletedAtUtc` buffer handling (F-TRF-009 technical notes).
- Telemetry: `transfer.deleted` (`{transferId, reason:"user"}`, TA-5.3).

## Links

- Feature: `TRF-009-my-files.md` (FR-009-3)
- Plan AC: AC-009-2
- Related: US-011-02 (admin force-delete is the same command)
- Architecture: TA-4.2#17, TA-5.3
- Milestone: T-022
