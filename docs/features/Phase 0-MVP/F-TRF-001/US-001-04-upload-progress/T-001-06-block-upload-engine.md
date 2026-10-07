# T-001-06 — UploadEngine.start: block-blob upload (8 MiB, parallelism 4, files serialized)

**Story:** US-001-04 | **Spec:** FR-001-4/7, TA-8.3 | **Size:** M
**Depends on:** T-001-05 (draft endpoint — provides the per-file SAS URLs), T-001-01 (store contract)

---

## Context to read (only these)

- `US-001-04-upload-progress.md` → happy path + Technical notes
- `../../F-TRF-001-upload-surface.md` → FR-001-4/7 + Technical notes (TA-8.3 line)

## Instructions

1. Implement **`UploadEngine.start(draft)`** in the store using `@azure/storage-blob`: per file, `BlockBlobClient.uploadData(file, { blobSize: 8 MiB blocks })` — block size **8 MiB**, parallelism **4 per file**, files **serialized** (one at a time).
2. Uploads above `CHUNK_THRESHOLD` (default 10 MB) are never a single PUT (FR-001-7) — the block path is the only path.
3. Per-file status transitions: `queued → uploading → done`; progress numbers come from block callbacks (bytes uploaded, not blocks).
4. Successful blocks are kept on failure (block-level resume within session) — a re-run of `start`/`retry` for the same file skips completed blocks.

## Exit check

- [ ] A 25 MB file uploads via multiple 8 MiB blocks (network trace shows >1 block PUT per file, never one giant PUT)
- [ ] Files upload strictly one at a time; a file reaches `done` before the next starts
- [ ] Killing the network mid-file and re-running resumes from the last completed block — finished bytes are not re-uploaded

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (wa.web: Vite 5 + React 18 + TS strict; @azure/storage-blob added for this task).
Task T-001-06 — implement the block upload engine.
Read first (only): docs/features/Phase 0-MVP/F-TRF-001/US-001-04-upload-progress/US-001-04-upload-progress.md (happy path + Technical notes) and F-TRF-001-upload-surface.md (FR-001-4/7).
Do exactly:
1. Implement UploadEngine.start(draft) using @azure/storage-blob: per file, BlockBlobClient.uploadData with 8 MiB blocks — parallelism 4 per file, files serialized one at a time.
2. Keep the block path as the only path for uploads above CHUNK_THRESHOLD (default 10 MB) — never a single PUT (FR-001-7).
3. Drive status transitions queued → uploading → done and progress numbers from block callbacks (bytes uploaded, not blocks).
4. Keep successful blocks on failure so re-running start/retry resumes from the last completed block within the session.
Done when: AC-001-1's upload half holds — 25 MB+ files go out as multiple 8 MiB blocks, files serialize, and a mid-file network kill resumes without re-uploading finished bytes.
Constraints: TA-8.3 constants exactly (8 MiB / parallelism 4 / serialized) — no config knobs at M0; retry/backoff logic is T-001-08's scope, not here.
```
