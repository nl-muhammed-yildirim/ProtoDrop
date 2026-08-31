# F-TRF-001 — Upload Surface (Chunked Upload)

**Priority:** P0 (MVP) | **Phase:** 0 — Core Transfer
**Spec source:** `02-feature-plan.md` F-TRF-001 | **Architecture:** TA-4.2#1–2, TA-6.8, TA-7.1, TA-8.3
**Milestone tasks:** T-009, T-012

---

## Description

The landing page **is** the upload surface: one full-viewport drag-and-drop target, no hero, no feature grid, no marketing. A guest (or signed-in user) selects one or more files — by dragging, clicking, or pasting an image — and sees every file staged in a list with per-file and overall byte-accurate progress. The browser uploads **blocks directly to Azure Blob Storage** via short-lived SAS URLs; `wa-api` never proxies file bytes. When every file reaches 100 %, the "Send." button enables and takes the user to the link screen (F-TRF-002).

**Actors:** guest sender, account user (free/Pro).
**Value:** zero-friction onboarding is the product's #1 differentiator; progress visibility removes all uncertainty while large files upload.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-001-1 | Landing page is a single full-viewport drag-and-drop surface. No hero, no feature grid. Drop zone accepts via drag, click-to-browse, and paste (image). |
| FR-001-2 | Multi-file selection. Files are added to a staging list; user removes individual files before final "Send." |
| FR-001-3 | Total selected size is validated against the plan limit (`MAX_TRANSFER_SIZE`) **before upload starts**; per-file size limit also enforced (`MAX_SINGLE_FILE`). |
| FR-001-4 | Upload is **chunked** (block blobs via `@azure/storage-blob`), block size 8 MiB, parallelism 4 per file, files serialized. |
| FR-001-5 | Progress UI shows per-file % and overall % based on **bytes uploaded**, not blocks. |
| FR-001-6 | Upload failure on a block → retry that block up to 5 times with backoff; then mark the file failed with a per-file "Retry" button; successful blocks are kept (block-level resume within session). |
| FR-001-7 | Uploads above `CHUNK_THRESHOLD` (default 10 MB) are never a single PUT. |

## Acceptance criteria

```gherkin
AC-001-1: Guest drops 3 files totaling 2 GB
  When upload completes
  Then all files show 100% and "Send" becomes enabled

AC-001-2: User selects files exceeding MAX_TRANSFER_SIZE
  When total exceeds the limit before Send
  Then a message names the limit and upload has not started
  And no bytes were written to storage

AC-001-3: Network drops mid-upload
  When a block fails after 5 retries
  Then that file is marked failed, other files unaffected, and a Retry button appears

AC-001-4: User drops an empty folder
  Then it is silently skipped with a toast "Some files were skipped"
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-001-1 | File renamed on disk after selection | Browser hands a `File` object — upload continues; UI copy: "we copy the file now" |
| EC-001-2 | Duplicate file names in one transfer | Keep both, names preserved (collision is the recipient's zip concern — F-TRF-004) |
| EC-001-3 | 0-byte file | Accept, but flag in file list |
| EC-001-4 | HEIC / unknown MIME | Accept any MIME; do not sniff |

## UI notes (UI-Reference §4.1, §4.2, §5.1)

- Drop zone: dashed `--border` idle → solid `--accent` on drag-over, bg `--accent-soft`.
- Visible **Choose files** button (keyboard path, F-TRF-016-2).
- Staging list: file rows (icon, name, size, remove ✕); total line "N files · X GB"; footer **Send.** primary button.
- Progress: 3 px `--accent` bar per row + overall line; failed row shows `--danger` text + **Retry**.
- Mobile: `<input type=file>` picker instead of drag (F-TRF-017).

## Technical notes

- `POST /api/v1/transfers/draft` (metadata only: names, sizes, contentTypes) → `{ draftId, expiresInSec, files[{fileId, uploadUrl(cwr SAS, 2 h)}] }` (TA-4.2#1, TA-3.6).
- `FinalizeTransferCommand` (MediatR, T-010): server-side blob copy `staging/… → transfers/{id}/files/{fileId}`, creates `Transfer(0)` + `FileItem` + `BlobRef(RefCount=1)`.
- Staging blobs older than 24 h without finalize → Blob lifecycle deletion (TA-3.5).
- Telemetry: `upload_started`, `upload_completed` (bytes, durationMs, retries), `upload_failed`.
- Limits: read from `ILimitsProvider` (TA-3.4) — never literals.

## Test plan

- Unit: UploadEngine state machine (queued→uploading→done/failed), progress math (bytes-based), retry/backoff counter.
- Integration (API): draft creation returns per-file SAS; limit pre-check at API (AC-001-2 server side); idempotent finalize.
- E2E (Playwright): AC-001-1…001-4; injected network failure → AC-001-3; retry button restores upload.

## User stories

| ID | Story | File |
|---|---|---|
| US-001-01 | Select files with drag & drop, click, or paste | `US-001-01-select-files.md` |
| US-001-02 | Stage and remove multiple files | `US-001-02-stage-files.md` |
| US-001-03 | Get warned before upload exceeds the limit | `US-001-03-size-validation.md` |
| US-001-04 | Watch per-file and overall progress | `US-001-04-upload-progress.md` |
| US-001-05 | Recover from a failed upload | `US-001-05-retry-upload.md` |
