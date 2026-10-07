# T-009-06 — Delete confirm modal + row actions (copy / view / re-send) (FR-009-3)

**Story:** US-009-04 (+ US-009-01 row actions) | **Spec:** FR-009-3, AC-009-2 (client half), EC-009-2 | **Size:** S
**Depends on:** T-009-05 (My Files screen — the rows + trash action render here), T-009-04 (DELETE endpoint 17 — the modal posts to it)

---

## Context to read (only these)

- `US-009-04-delete.md` → happy path + Alternative flows + UI notes
- `../../F-TRF-009-my-files.md` → FR-009-3 + AC-009-2 + EC-009-2 + UI notes (confirm copy)

## Instructions

1. Add the **row actions** to each My Files row as ghost buttons: **Copy link**, **View** (recipient page), **Re-send** (link icon — lands in F-TRF-010's T-023; wire the route now, endpoint later), **Delete** (trash).
2. **Delete confirm modal** (per UI-Reference §4.5, danger button): copy names exactly what's going away — **"Delete transfer? 3 files, 1.2 GB. This can't be undone."** (file count + total size from the row data — AC-009-2).
3. Confirm → `DELETE /transfers/{id}`; on success remove the row locally + toast **"Transfer deleted."** (`role=status`). **`NOT_FOUND` is treated as success** (EC-009-2: two tabs deleting the same transfer — no error toast, row quietly vanishes).
4. Cancel → nothing sent; row unchanged.

## Exit check

- [ ] Delete a 3-file / 1.2 GB transfer → modal says exactly "Delete transfer? 3 files, 1.2 GB." (AC-009-2 client half)
- [ ] Confirm → row gone + success toast; recipient page no longer serves files
- [ ] Simulated double delete (second tab / retry) → `NOT_FOUND` treated as success — no error toast
- [ ] Copy link copies the transfer URL to the clipboard; View opens the recipient page in a new context

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS; My Files screen + DELETE endpoint 17 in place).
Task T-009-06 — add the row actions + delete confirm modal (milestone task T-022, part 6).
Read first (only): docs/features/Phase 0-MVP/F-TRF-009/US-009-04-delete/US-009-04-delete.md (happy path + Alternative flows + UI notes) and F-TRF-009-my-files.md (FR-009-3).
Do exactly:
1. Add the row actions to each My Files row as ghost buttons: Copy link, View (recipient page), Re-send (link icon — lands in F-TRF-010's T-023; wire the route now, endpoint later), Delete (trash).
2. Delete confirm modal (per UI-Reference §4.5, danger button): copy names exactly what's going away — "Delete transfer? 3 files, 1.2 GB. This can't be undone." (file count + total size from the row data — AC-009-2).
3. Confirm → DELETE /transfers/{id}; on success remove the row locally + toast "Transfer deleted." (role=status). NOT_FOUND is treated as success (EC-009-2: two tabs deleting the same transfer — no error toast, row quietly vanishes).
4. Cancel → nothing sent; row unchanged.
Done when: AC-009-2 holds in the browser — the confirm names count + size, delete removes the row with a success toast, and a NOT_FOUND response is swallowed as success.
Constraints: UI per UI-Reference §5.4 (ghost action buttons); "immediate" for the user = row gone — physical blob cleanup runs on the server's refcount path; Re-send endpoint lands later (T-023) — route stub only for now.
```
