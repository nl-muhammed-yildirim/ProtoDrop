# F-TRF-004 — Download All (ZIP)

**Priority:** P0 (MVP) | **Phase:** 0 — Core Transfer
**Spec source:** `02-feature-plan.md` F-TRF-004 | **Architecture:** TA-6.6, TA-4.2#7, TA-3.5
**Milestone tasks:** T-017

---

## Description

Many recipients want *the whole set*, not a one-file-at-a-time loop. "Download all" produces a single ZIP of the transfer: **generated server-side on demand** by the `f-zip` Azure Function, **cached for the life of the transfer** (transfers are immutable in MVP, so the cache never goes stale), and delivered to the browser through the same time-boxed SAS mechanism as single-file downloads. Generation is streamed (never buffered whole), capped by `MAX_ZIP_SIZE`, and async — first click may return 202 + polling while the zip is being built.

**Actors:** recipient (any device).
**Value:** one click = the whole transfer; identical UX on desktop and phone.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-004-1 | "Download all" is available **only when the transfer has ≥2 files**. Single-file transfers show only the per-file Download button. |
| FR-004-2 | The ZIP is generated **server-side on demand** by `f-zip`: reads the transfer's files (ordered by `SortOrder`), writes `transfers/{id}/all.zip`, returns its download URL. |
| FR-004-3 | The generated ZIP is **cached for the life of the transfer** at a fixed path; it is regenerated only when the transfer is re-sent (new `transferId` → new path). |
| FR-004-4 | Cap: `MAX_ZIP_SIZE` (Free default 4 GB, = sum of file sizes). If a transfer exceeds it, "Download all" is hidden and an inline note explains; per-file download remains available. |
| FR-004-5 | ZIP entries use the **original file names**, top-level (no folders). Name collisions inside the zip get `_1`, `_2` suffixes (deterministic order by `SortOrder`). |
| FR-004-6 | Generation is **async**: `GET .../download-all-url` returns `200 { url }` when ready, `202 { pollAfterSec: 2 }` while generating; the UI polls every 2 s and shows "Preparing your download…". |

## Acceptance criteria

```gherkin
AC-004-1: 3-file transfer, click Download all
  Then a zip is generated once
  And a second recipient (or same user, second click) gets the cached zip instantly
  And exactly one zip blob exists for the transfer

AC-004-2: Transfer totals 6 GB
  Then "Download all" is hidden with note "Files are large — download individually"

AC-004-3: Two staged files named "report.pdf"
  When the zip is generated
  Then the zip contains "report.pdf" and "report_1.pdf" with intact contents
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-004-1 | Very large totals near `MAX_ZIP_SIZE` | Stream through a 4 MB buffer (`ZipArchive` streaming mode), never buffer the whole zip (TA-6.6) |
| EC-004-2 | Transfer expires while the zip is generating | `finally` deletes the partial zip; next request regenerates cleanly |
| EC-004-3 | Two first-clickers race | Both get 202; `f-zip` concurrency (4) with path-idempotency → one zip wins, the other observes the existing blob and returns its URL |
| EC-004-4 | iOS Safari zip quirk | Safari shows a download bar for the zip; if the user cancels, the per-file buttons remain the fallback |

## UI notes (UI-Reference §5.3)

- **Download all**: primary button, shown above/below the file list when applicable (≥2 files); per-file **Download** buttons always present.
- Generating state: button → "Preparing your download…" (spinner, disabled); poll indicator in `--fs-small`.
- Cap note (FR-004-4): `--fs-small`, `--fg-muted`, directly under the file list: "Files are large — download individually."
- Failure after 3 min of polling: revert to idle + toast "Download preparation failed — try again" (error toast, `role=alert`).

## Technical notes

- Function: `f-zip` (TA-6.2, HTTP trigger, 4-way concurrency, 60-min timeout). Trigger path: `POST /zip/{transferId}` behind Front Door with `x-zip-sign` shared-secret header (TA-9.3).
- Idempotent by path: `transfers/{id}/all.zip` (TA-3.5); existence check before generation.
- Streaming: `ZipArchive` on the blob upload stream, 4 MB buffer, `leaveOpen=true` (TA-6.6); entries = `OriginalName` with `_1`/`_2` de-dup.
- Cap check **before** starting: `SUM(FileItem.SizeBytes) > MAX_ZIP_SIZE` → 413 (via endpoint 7 mapping) / UI hides the button from metadata (`hasDownloadAll=false`, TA-4.2a).
- Delivery: minted read-SAS (30 min, TA-3.6) on `all.zip`; `download.completed` event carries `fileId=null` (download-all).
- Telemetry: `zip_generated` (sizeBytes, durationMs), `zip_failed` (TA-10.2).

## Test plan

- Unit: zip entry-name de-dup logic (`report.pdf`, `report.pdf` → `report.pdf`, `report_1.pdf`); cap arithmetic.
- Integration: endpoint 7 — first call 202 then 200; second call 200 immediately; exactly one `all.zip` blob exists; 413 when sum > `MAX_ZIP_SIZE`; `MAX_DOWNLOADS_REACHED` respected (cap check at endpoint 7, TA-7.2 step 5).
- E2E: AC-004-1, AC-004-2 (Playwright + one manual iOS check).
- Perf: 4 GB zip generation < 60 s on dev (regression guard; streaming, not buffering).

## User stories

| ID | Story | File |
|---|---|---|
| US-004-01 | Generate a zip of the whole transfer | `US-004-01-generate-zip.md` |
| US-004-02 | Get the cached zip instantly on later requests | `US-004-02-cached-zip.md` |
| US-004-03 | See why "Download all" is unavailable | `US-004-03-zip-cap.md` |
