# F-TRF-009 — My Files (History)

**Priority:** P0 (MVP) | **Phase:** 0 — Core Transfer
**Spec source:** `02-feature-plan.md` F-TRF-009 | **Architecture:** TA-4.2#14–15,17, TA-3.2 `IX_Transfer_Owner`
**Milestone tasks:** T-022

---

## Description

The reward for signing in: **My Files** lists every transfer the user created — most recent first, with everything needed to act: file count, total size, recipient count, status, expiry countdown, download count. Cursor-paginated (25/page), filterable (All / Active / Expired), with row actions: copy link, view, re-send (F-TRF-010), and delete (immediate, with a confirm that names what's going away). An empty state invites the first transfer.

**Actors:** signed-in user.
**Value:** "where are my files?" answered in one screen — the retention engine of the product.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-009-1 | "My Files" list: transfers the user created, most recent first. Each row: file count, total size, recipient count, status chip, expiry countdown (or "Expired {date}"), download count. |
| FR-009-2 | Pagination: cursor-based (`?status=&cursor=`), 25/page (TA-4.1.4). |
| FR-009-3 | Row actions: **Copy link**, **View** (recipient page), **Re-send** (F-TRF-010), **Delete** (confirm modal naming file count + size; deletes blobs + row immediately, no grace — owner's explicit intent). |
| FR-009-4 | Filter pills: **All / Active / Expired** (`Expired` includes `DownloadLimit` and `Deleted`-by-time states). |
| FR-009-5 | Empty state: icon + "No transfers yet." + CTA to the upload surface (`/`). |

## Acceptance criteria

```gherkin
AC-009-1: User with 30 transfers
  Then page 1 shows 25, ordered by CreatedAtUtc desc

AC-009-2: Delete a transfer
  Then the confirm dialog names the file count and size
  And after delete, the row is gone and blobs removed

AC-009-3: Expired row
  Then the countdown is replaced by "Expired {date}"
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-009-1 | Deleting while a download is in progress | Allowed; the recipient's in-flight 30-min SAS still works (SAS outlives the row — documented) |
| EC-009-2 | Two browser tabs delete the same transfer | Idempotent delete: second request gets `NOT_FOUND` (treated as success, no error toast) |
| EC-009-3 | Delete of a transfer that's also the target of a pending re-send | Re-send finalize later fails with `FILES_GONE` (F-TRF-010) — the draft screen shows the error |
| EC-009-4 | Recipient count of 0 (link-only) | Shows "Link only" in the recipients column |

## UI notes (UI-Reference §5.4)

- Header: "My Files" + **New transfer** primary (→ `/`).
- Filter pills row: All / Active / Expired (`--accent-soft` active pill).
- Row: name (first file name or "Transfer"), file count, size, recipients count, status chip (green active / amber expiring soon / gray expired), expiry countdown, downloads ("87/100"), actions as ghost buttons (copy, view, re-send, delete/trash).
- Delete confirm modal: "Delete transfer? 3 files, 1.2 GB. This can't be undone." (names count + size per AC-009-2).
- Cursor paging: "Prev / Next" (TA-4.1.4); empty state per FR-009-5.

## Technical notes

- `ListMyTransfersQuery` (endpoint 14): `WHERE OwnerAppUserId=@me [AND status filter] ORDER BY CreatedAtUtc DESC`, cursor `base64(created_at:id)` (TA-4.1.4); join-free counts (`FileCount`, `DownloadsCount` columns on `Transfer`; recipient count = `COUNT(EmailRecipient)` subquery or cached column).
- `GetMyTransferQuery` (endpoint 15): detail incl. recipient list.
- `DeleteTransferCommand` (endpoint 17): same semantics as admin delete; `transfer.deleted` with reason `user`; `BlobRef` refcount handling (F-TRF-005 jobs finish the physical cleanup — a user delete marks `Deleted` and lets the jobs run; "immediate" means the row is gone for the user, blobs follow the refcount path).
  - Note: to keep "blobs removed" honest for a sole reference, user-delete also sets `PhysicallyDeletedAtUtc = now + 24h` when refcount hits 0 (same buffer as expiry path).
- Status chip mapping: `Active` (green) / `DownloadLimit` (amber) / `Expired` (gray) / `Deleted` (hidden unless All filter includes a `showDeleted` — MVP: deleted rows disappear from the list).

## Test plan

- Unit: cursor encode/decode; status filter mapping; chip state resolution.
- Integration: 30 seeded transfers → page 1 = 25 (AC-009-1); filter Active excludes expired; delete → row gone + `transfer.deleted` + refcount decremented; double delete → `NOT_FOUND` (EC-009-2); guest (no owner) transfer not listed.
- E2E: My Files CRUD happy path (T-022 exit; T-028 e2e suite).

## User stories

| ID | Story | File |
|---|---|---|
| US-009-01 | Review my transfers | `US-009-01-history-list.md` |
| US-009-02 | Filter my transfers | `US-009-02-filters.md` |
| US-009-03 | Re-send a transfer from My Files | `US-009-03-resend.md` |
| US-009-04 | Delete one of my transfers | `US-009-04-delete.md` |
