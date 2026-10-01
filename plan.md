# Plan — US-001-04: Watch per-file and overall upload progress

## 1. Goal

Implement live, byte-accurate upload progress for the ProtoDrop landing page (F-TRF-001 / T-012), per `docs/features/Phase 0-MVP/F-TRF-001/US-001-04-upload-progress.md`:
- After "Send.", each staged file row shows a **3 px progress bar + percentage** computed from **bytes uploaded, not block count** (FR-001-5).
- An **overall line** reports combined uploaded bytes vs total bytes ("Uploading… 1.2 GB of 4.0 GB").
- A file at 100 % is marked **done** (check icon) and the next file starts (files serialized — FR-001-4).
- All files done → overall shows 100 %.
- Progress is driven by the existing Zustand store (`useUploadEngine`) — **no polling**.

## 2. Analysis

### What already exists (verified against codebase)

| File | State |
|---|---|
| `src/wa.web/src/core/upload/UploadEngine.ts` | TA-8.3 Zustand store: `{ files:[{id,name,size,status:'queued'\|'uploading'\|'done'\|'failed',progress}], overall:{sentBytes,totalBytes} }`, API `addFiles/remove/reset/start(draft)/retry(fileId)`. Module-level `fileHandles` Map holds original `File` objects. **`start(_draft)` currently only flips every file to `'uploading'` — no real upload, no progress callbacks.** This is the seam US-001-04 extends. |
| `src/wa.web/src/features/landing/StagingList.tsx` | UI §4.2 rows (icon, name, size, remove ✕), violation message, Send button that POSTs `/api/v1/transfers/draft` then calls `start(data)`. **No progress bar / percent label / overall line yet.** |
| `src/wa.web/src/core/upload/formatBytes.ts` | 1024-based formatter (TA-8.5), already imported by StagingList — reuse for the overall line + labels. |
| `src/wa.web/src/styles/landing.css`, `tokens.css` | Tokens: `--accent`, `--success`, `--fs-tiny`, `--fs-small`. Existing `.file-row*` styles to extend. |

### What is missing for this slice
- **Real uploader behind `start(draft)`**: serialized per-file block upload (8 MiB blocks, parallelism 4 within a file) that emits byte callbacks into the store.
- **Byte-based progress math** (FR-001-5): per-file `progress = uploadedBytes / size`; overall `sentBytes = Σ uploaded bytes`, recomputed on every callback.
- **Per-row UI**: 3 px bar + percent label; done check icon.
- **Overall line**: "Uploading… X of Y" in `--fs-small`.
- **ARIA live region** (`role=status`, polite) announcing overall changes (not every tick — F-TRF-016-3).

### Scope decision (critical — keep it shippable and testable)
The story's technical notes call for `@azure/storage-blob` `BlockBlobClient.uploadData`. To keep this slice focused on **progress state + math + UI** while still being the real transport path:
- Implement the uploader in `UploadEngine.ts` using a small, **injectable upload function** (module-level seam) so unit tests can simulate byte callbacks without a network. The default implementation uses `@azure/storage-blob`'s progress callback (cumulative bytes). If the package is not yet installed in `wa.web`, add it (`npm i @azure/storage-blob`) — matches FR-001-4/FR-001-7 and TA-8.3.
- **Do NOT implement US-001-05** (block-level retry/backoff 5×, `failed` status, Retry button, resume from last completed block). Leave a clear TODO seam so that story owns failure semantics. Per-file errors are caught; for now the file stays at its last known state with a TODO(US-001-05/T-012) comment.

### Draft shape (from existing StagingList test mock — TA-4.2#1)
```json
POST /api/v1/transfers/draft → { "draftId": "...", "expiresInSec": 7200,
  "files": [ { "uploadUrl": "…", "contentLength": N } ] }
```
The uploader matches draft files to staged files **by index/order** (the full DTO with `fileId` lands with US-001-03/T-009; for now order-based matching is the contract).

## 3. Files to Modify or Create

| File | Action | Why |
|---|---|---|
| `src/wa.web/src/core/upload/UploadEngine.ts` | **Modify** | Replace `start()` stub with a real serialized uploader; add byte-based progress math (pure, exported for tests); keep TA-8.3 state shape unchanged. |
| `src/wa.web/src/features/landing/StagingList.tsx` | **Modify** | Render per-row 3 px bar + percent label; done check icon; overall line "Uploading… X of Y"; ARIA live region (throttled). |
| `src/wa.web/src/styles/landing.css` | **Modify** | Add `.file-progress`, `.file-percent`, `.staging-overall`, `.file-done-icon` using tokens (`--accent`, `--success`, `--fs-tiny`, `--fs-small`). |
| `src/wa.web/src/core/upload/UploadEngine.test.ts` | **Modify** | Unit tests: byte-based progress math, per-file completion → next file starts (serialization), overall recompute on every callback, all-done → 100 %, zero-byte file guard. |
| `src/wa.web/src/features/landing/StagingList.test.tsx` | **Modify** | Component tests: bar + percent render from store state; overall line text format; done row shows check icon; live region present. |
| `package.json` (wa.web) | **Possibly modify** | Add `@azure/storage-blob` if not present (real transport). Confirm before adding — the injectable seam keeps unit tests independent of it. |

## 4. Dependencies / Cross-cutting Impact

- **TA-8.3 state contract is frozen.** Do NOT change the shape of `files[]`, `overall`, or API names (`start/retry/remove/reset`). US-001-05 (retry) and F-TRF-002 (finalize) depend on it.
- **`fileHandles` Map** already stores original `File` objects — use these for upload; do not re-read from disk.
- **Progress must be bytes-based.** A per-block callback that only counts blocks violates FR-001-5. The Azure SDK progress callback reports cumulative bytes transferred — use it directly.
- **Files are serialized** (FR-001-4): upload file i fully before starting file i+1. Parallelism 4 applies *within* a single file only.
- **No polling.** UI reads come from Zustand selectors; the uploader calls `set()` on each callback.
- **ARIA live region** must announce overall changes (e.g., every ~5 % or at done/failed transitions), not every tick — otherwise screen readers are flooded (F-TRF-016-3).
- **Existing test in StagingList.test.tsx** asserts that after Send, all files become `'uploading'` immediately. `start()` must flip files to `'uploading'` **synchronously** before any async upload work begins — keep that assertion passing.
- **Windows/PowerShell 5.1** dev box: composite gates wrapped in `cmd /c "npm run lint && npm run test:run && npm run build"`; commit messages ASCII (no em-dash); judge success by per-stage output, not wrapper exit code.

## 5. Implementation Steps

### Step 1 — `UploadEngine.ts`: real uploader behind the existing seam
- Keep `start(draft)` signature and its **synchronous** flip of every file to `'uploading'` (preserves the existing Send test).
- After the synchronous flip, kick off an async serialized loop over staged files **in order**. For each file:
  - Get its `File` from `fileHandles`.
  - Upload via Azure `BlockBlobClient.uploadData(file, size, { blockSize: 8*1024*1024, maxParallelism: 4 })` to the per-file SAS URL (draft provides `{files:[{uploadUrl}]}` — match by index/order; if a file has no URL in tests, treat as a no-op transport).
  - On each progress callback (cumulative bytes `loadedBytes`): set that file's `progress = loadedBytes / size` (guard zero-byte: `size === 0 ? 1 : …`) and recompute `overall.sentBytes = Σ uploadedBytes across all files`; keep `totalBytes = Σ sizes`.
  - When a file reaches 100 %: set its status to `'done'`, progress 1, then start the next file (serialization).
- **Pure byte-math helper** (exported for unit tests): given an array of `{size, uploadedBytes}`, return per-file progress fractions + overall sent/total. This isolates FR-001-5 from transport.
- Keep `retry(fileId)` as a stub with a TODO(US-001-05/T-012) — do not invent backoff here.
- **Testability:** make the upload function injectable (module-level seam, e.g., `let uploadFn = defaultUploader` or an optional param on `start`). Unit tests simulate byte callbacks without a network. Do not over-engineer — a simple injectable suffices for this story's test plan ("Unit: UploadEngine state machine, progress math").

### Step 2 — `StagingList.tsx`: render progress
- For each row (only when status is `'uploading'` or `'done'`; before Send keep the plain icon+name+size layout):
  - Render a **3 px progress bar** element with CSS width = `${Math.round(progress*100)}%`, background `--accent`.
  - Render a **percent label** (`--fs-tiny`) showing `Math.round(progress*100)` + "%".
- For rows with status `'done'`: show a **check icon** (inline SVG, `aria-hidden`), colored `--success`, plus name and size — per the spec's done-row form ("Done row: --success check icon, name, size").
- Add an **overall line** above the list: text `"Uploading… {formatBytes(sent)} of {formatBytes(total)}"` in `--fs-small`. Show it while any file is uploading; when all are `done`, show 100 % / total-of-total.
- Wrap a dedicated span in an **ARIA live region**: `<div role="status" aria-live="polite">…</div>`. Update its text only on coarse changes (e.g., every 5 % or at done transitions) to avoid tick spam — track the last announced bucket via a `useRef`.
- Reuse existing `formatBytes` helper (already imported).
- Do **not** change the Send button logic, violation handling, or draft POST — those are US-001-03 and already work.

### Step 3 — `landing.css`: new styles using tokens
- `.file-progress { height: 3px; background: var(--accent); border-radius: … ; }` (width set inline).
- `.file-percent { font-size: var(--fs-tiny); color: var(--fg-muted); flex-shrink: 0; }`.
- `.staging-overall { font-size: var(--fs-small); color: var(--fg-muted); text-align: center; padding: 4px 8px; }`.
- `.file-done-icon { width/height ~16px; color: var(--success); flex-shrink: 0; }`.
- Keep consistent with existing `.staging*` classes and spacing (gap 8 px).

### Step 4 — Tests
- **Unit (`UploadEngine.test.ts`)** using the injected uploader / byte-callback simulator:
  - Per-file progress reflects bytes uploaded (e.g., upload 512 of 1024 → progress 0.5, overall.sentBytes = 512).
  - A file at 100 % is marked `done` and the next file begins uploading (serialization).
  - Overall recompute on every callback: with 3 files, after partial bytes across files, overall.sentBytes equals Σ uploaded bytes; totalBytes unchanged.
  - All files done → all status `done`, overall shows 100 % (sent == total).
  - Byte-based not block-count: a file whose size is not a multiple of the block size still reaches exactly 1.0 at completion.
  - Zero-byte file guard: progress = 1 and status `done` immediately on start (no divide-by-zero).
- **Component (`StagingList.test.tsx`)**:
  - Given store state with one `uploading` file at progress 0.5, render shows a bar (width ~50 %) and a "50%" label.
  - Overall line text matches `formatBytes(sent)` + " of " + `formatBytes(total)` for the staged set.
  - A `done` row renders the check icon (assert by class / accessible structure).
  - Live region exists with `role="status"`.

### Step 5 — Update PROGRESS.md (last step)
Mark US-001-04 completed, carry forward AC clauses to US-001-05 / T-012.

## 6. Risks & Considerations

| Risk | Mitigation |
|---|---|
| **Azure dependency** — `@azure/storage-blob` may not yet be in `wa.web`. | Make transport injectable so unit tests don't need the real client; only integration/E2E needs the SDK. Confirm with user before `npm i`. |
| **Don't break US-001-05** — retry/backoff/`failed`/Retry button belong to that story. | Leave a TODO seam in `start()`'s per-file error path; don't introduce the 5× backoff loop or resume-from-last-block here. |
| **Don't break existing "Send → all uploading" test** — `start()` must flip files to `'uploading'` synchronously before async work. | Flip status in the same synchronous `set()` that the current stub does; kick off the async loop after. |
| **Byte-based progress (FR-001-5)** — a block counter passes happy path but fails non-multiple sizes. | Use cumulative bytes from the SDK callback, never block index/count. Test with a size not a multiple of 8 MiB. |
| **Serialization vs parallelism** — getting this wrong changes throughput/UX and violates FR-001-4. | Files serialized (one at a time); parallelism 4 only *within* one file via the SDK's `maxParallelism`. |
| **ARIA live region flooding** — announcing every tick spams screen readers (F-TRF-016-3). | Throttle to coarse buckets or transitions only; track last announced bucket with a ref. |
| **Zero-byte file (EC-001-3)** — divide-by-zero on `loaded/size`. | Guard `progress = size === 0 ? 1 : loaded/size`; mark done immediately on start. |
| **Paused tab / slow connection** — block-blob upload survives; no overall timeout. | The callback drives state; no timer-based recompute needed. No overall timeout (only block-level retries, US-001-05). |
| **PowerShell/Windows dev box** — composite gates + ASCII commits. | `cmd /c "npm run lint && npm run test:run && npm run build"`; judge success by per-stage output, not wrapper exit code. |

## 7. Final Developer Prompt

```text
Task: US-001-04 — Watch per-file and overall upload progress
Story: docs/features/Phase 0-MVP/F-TRF-001/US-001-04-upload-progress.md
Feature F-TRF-001 | Story ACs below are the acceptance criteria — encode them in tests.

## What you are building

Live, byte-accurate upload progress on the landing page:
  • Per-file live percentage computed from BYTES uploaded (not block count) — FR-001-5.
  • Overall line "Uploading… {sent} of {total}" using formatBytes().
  • A file at 100 % is marked done (check icon); the next file starts (files serialized — FR-001-4).
  • All files done → overall shows 100 %.
  • Progress is driven by the existing Zustand store (useUploadEngine) — NO polling.

The real transport uses @azure/storage-blob BlockBlobClient.uploadData (8 MiB blocks, maxParallelism 4 per file), but keep it injectable so unit tests simulate byte callbacks without a network. US-001-05 owns failure/retry semantics — leave a TODO seam for that story.

## Acceptance criteria (story, verbatim)

Happy path:
  Given I have 3 files staged and I press "Send."
  When the upload runs
  Then every file row shows a live percentage based on bytes uploaded
  And the overall line shows combined progress
  When all files reach 100%
  Then "Send." advances to the link screen (F-TRF-002 — out of scope here; reaching done/100 % is enough)

  Given a file row is at 100%
  When the next file starts
  Then the completed row shows a done check and the next row begins filling

Edge cases:
  • The progress state is live (Zustand store), so no polling.
  • A failed file pauses only its own row (US-001-05); overall % keeps counting the completed bytes.
  • If the user leaves the page mid-upload, the session is lost in MVP (block-level resume is within-session only, FR-001-6).

UI notes:
  • Per-row bar: 3 px, --accent, percent in --fs-tiny (UI-Reference §4.2).
  • Overall line above the list: "Uploading… 1.2 GB of 4.0 GB" in --fs-small.
  • ARIA live region (role=status, polite) announces overall changes, not every percent tick (F-TRF-016-3).
  • Done row: --success check icon, name, size.

## Scope boundaries

### In scope (this slice)
- Replace the start(draft) stub in UploadEngine.ts with a real serialized uploader:
    * Synchronously flip every file to 'uploading' first (preserves existing Send test).
    * Then async loop over staged files IN ORDER (serialization — FR-001-4). For each file, upload via the injectable upload function using @azure/storage-blob BlockBlobClient.uploadData(file, size, { blockSize: 8*1024*1024, maxParallelism: 4 }) to the per-file SAS URL (draft.files[i].uploadUrl — match by index/order).
    * On each progress callback (cumulative bytes loadedBytes): set file.progress = loadedBytes / size (guard zero-byte: size === 0 ? 1 : …) and recompute overall.sentBytes = Σ uploadedBytes across all files; totalBytes = Σ sizes.
    * When a file reaches 100 %: status 'done', progress 1, then start the next file.
- Pure byte-math helper (exported for unit tests): given [{size, uploadedBytes}], return per-file progress fractions + overall sent/total. Isolates FR-001-5 from transport.
- Per-row UI in StagingList.tsx: 3 px bar (width = Math.round(progress*100)%) + percent label (--fs-tiny); done rows show --success check icon + name + size.
- Overall line above the list: "Uploading… {formatBytes(sent)} of {formatBytes(total)}" in --fs-small; shows 100 % when all files are done.
- ARIA live region (role="status", aria-live="polite") announcing overall changes only on coarse buckets (~5 %) or done transitions — throttle via useRef, not every tick.
- Unit tests: byte-based progress math, per-file completion → next file starts (serialization), overall recompute on every callback, all-done → 100 %, zero-byte guard, non-multiple-of-block-size still reaches exactly 1.0.
- Component tests: bar + percent render from store state; overall line text format "X of Y"; done row shows check icon; live region present (role="status").

### Out of scope (explicitly)
- US-001-05: block-level retry/backoff (5 × backoff), 'failed' status, Retry button, resume from last completed block. Leave a TODO(US-001-05/T-012) seam in start()'s per-file error path — catch errors, leave file at last known state.
- F-TRF-002: navigation to the link screen / FinalizeTransferCommand (T-010). Reaching done/100 % is enough here.
- Draft DTO full shape with fileId (US-001-03/T-009) — for now match draft.files[i] to staged files by index/order; treat as {files:[{uploadUrl, contentLength}]}.
- Telemetry upload_completed emission (TA-10.2) unless already wired — note as follow-up if not present.

## Existing assets you must preserve (do NOT refactor or replace)

  • src/wa.web/src/core/upload/UploadEngine.ts — TA-8.3 store; keep state shape {files:[{id,name,size,status,progress}], overall:{sentBytes,totalBytes}} and API names start/retry/remove/reset unchanged. Keep fileHandles Map for original File objects.
  • src/wa.web/src/features/landing/StagingList.tsx — existing rows + Send button + violation logic (US-001-03) already work; only ADD progress UI, do not touch the draft POST or error mapping.
  • src/wa.web/src/core/upload/formatBytes.ts — reuse for overall line + labels.
  • src/wa.web/src/styles/tokens.css and landing.css — use existing tokens (--accent, --success, --fs-tiny, --fs-small); no new token values invented.

## Technical contract (TA-8.3 / TA-4.2#1)

Draft response shape (from the existing StagingList test mock):
```json
POST /api/v1/transfers/draft → { "draftId": "...", "expiresInSec": 7200,
  "files": [ { "uploadUrl": "…", "contentLength": N } ] }
```
UploadEngine.start(draft) receives this. Match draft.files[i] to staged files by index/order (the full DTO with fileId lands with T-009).

Progress math (FR-001-5 — bytes-based, NOT block count):
  per-file progress = uploadedBytes / size   (zero-byte guard: size === 0 ? 1 : …)
  overall.sentBytes  = Σ uploadedBytes across all files
  overall.totalBytes = Σ sizes

Serialization (FR-001-4): upload file i fully before starting file i+1. Parallelism 4 applies WITHIN a single file only (SDK maxParallelism).

## Test requirements (vitest + @testing-library/react)

Unit — src/wa.web/src/core/upload/UploadEngine.test.ts (use the injectable uploader / byte-callback simulator):
  • Per-file progress reflects bytes uploaded: upload 512 of 1024 → progress 0.5, overall.sentBytes = 512.
  • A file at 100 % is marked done and the next file begins uploading (serialization).
  • Overall recompute on every callback: with 3 files, after partial bytes across files, overall.sentBytes equals Σ uploaded bytes; totalBytes unchanged.
  • All files done → all status done, overall shows 100 % (sent == total).
  • Byte-based not block-count: a file whose size is NOT a multiple of the 8 MiB block still reaches exactly 1.0 at completion.
  • Zero-byte file guard: progress = 1 and status done immediately on start (no divide-by-zero).

Component — src/wa.web/src/features/landing/StagingList.test.tsx:
  • Given store state with one uploading file at progress 0.5, render shows a bar (width ~50 %) and a "50%" label.
  • Overall line text matches formatBytes(sent) + " of " + formatBytes(total) for the staged set.
  • A done row renders the check icon (assert by class / accessible structure).
  • Live region exists with role="status".

Reuse makeFile from existing tests (create File with Object.defineProperty(file, 'size', { value })). Use useUploadEngine.getState().addFiles([...]) in each test. Reset store + toasts in beforeEach (existing pattern).

## Constraints (AGENT.md §4) — before marking complete, run the full gate:
  ```powershell
  dotnet test tests/wa.domain.unit
  dotnet test tests/wa.application.unit
  dotnet test tests/wa.api.integration
  cd src/wa.web && npm run lint && npm run test:run && npm run build
  ```
  (Windows/PowerShell 5.1: wrap composite gates in cmd /c "…"; commit messages ASCII — no em-dash; judge success by per-stage output, not wrapper exit code.)

Plus update docs/PROGRESS.md as the last step: mark US-001-04 completed, carry forward AC clauses to US-001-05 / T-012.

## Notes for the reviewer

The uploader is injectable so unit tests don't need a network; only integration/E2E needs @azure/storage-blob. If the package isn't yet in wa.web/package.json, add it (npm i @azure/storage-blob) — matches FR-001-4/FR-001-7 and TA-8.3. The per-file error path is a deliberate seam for US-001-05 (retry/backoff 5×, failed status, Retry button, resume from last completed block) — do NOT implement those here. Progress is bytes-based (cumulative loadedBytes from the SDK callback), never a block counter; this is what FR-001-5 checks and what the non-multiple-of-block-size test guards against.
```

---

**May I proceed with this plan?** If you approve, I will execute the implementation exactly as scoped above. If any part of the scope needs adjustment (for example, if you want US-001-05 retry semantics included in this slice, or prefer a simpler approach without the Azure transport and just simulate progress), tell me and I'll refine the prompt before we begin.
