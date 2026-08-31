# US-003-02 — Download all files in one zip

**Feature:** F-TRF-003 — Recipient Download Page | **Status:** pending

---

**Story:** As a recipient of a multi-file transfer, I want to get every file in a single zip with one click, so that I'm not downloading file by file.
**Actor:** Recipient (any device) on an active transfer with ≥2 files.
**Goal:** **Download all** → one zip, original file names, top-level entries, via the same time-boxed SAS as single downloads.

## Preconditions

- The transfer is `Active` and has ≥2 files (FR-004-1; single-file transfers show only per-file Download).
- Total file size ≤ `MAX_ZIP_SIZE` — otherwise the button is hidden with an inline note (FR-004-4; covered by US-004-03).

## Happy path

1. Recipient clicks **Download all**.
2. If the zip is not built yet: button shows "Preparing your download…" (spinner, disabled); the UI polls every 2 s (FR-004-6).
3. When ready (HTTP 200 + URL), the browser downloads `all.zip` — a zip with one top-level entry per file, original names (deterministic `_1`/`_2` de-dup on collision, FR-004-5).
4. The download uses a 30-minute read SAS (FR-003-3); the download counts toward `DownloadsCount` (F-TRF-005).

## Alternative flows

- **Zip already cached** (built by an earlier request): instant `200 { url }`, no "preparing" state (FR-004-3, US-004-02).
- **Cap exceeded** (`SUM(FileItem.SizeBytes) > MAX_ZIP_SIZE`): button never appears; inline note under the file list instead (US-003-05 / F-TRF-004-4).
- **Failure after 3 minutes of polling:** button reverts to idle + error toast "Download preparation failed — try again" (`role=alert`).

## Acceptance criteria

```gherkin
Given an active transfer with 3 files
When I click "Download all"
Then the button shows "Preparing your download…" while generation runs
And then a zip downloads whose entries are the 3 original file names at top level

Given two staged files named "report.pdf"
When the zip is generated
Then it contains "report.pdf" and "report_1.pdf" with intact contents

Given the zip was already generated for this transfer
When I (or another recipient) click "Download all"
Then the cached zip is served instantly
And exactly one all.zip blob exists for the transfer
```

## Edge cases

- 500 MB zip on flaky 3G: browser-native Range resume against the SAS URL (EC-003-1).
- iOS Safari: the zip triggers a visible download bar; per-file buttons remain the fallback (EC-004-4).
- "Download all" consumes **one** download against `DownloadsCount` (the zip, not each file inside).

## UI notes

- **Download all**: primary button, per file list (UI-Reference §5.3); generating state per F-TRF-004 UI notes.
- The "Downloads left: N" line (F-TRF-003-5) decrements visibly after a successful download-all.

## Technical notes

- Endpoint 7 → `f-zip` (TA-6.6): idempotent by path `transfers/{id}/all.zip`, 4 MB streaming buffer, `ZipArchive` (never buffers the whole zip).
- Delivery: minted 30-min read SAS on `all.zip`; `download_completed` event carries `fileId = null` (download-all).
- Telemetry: `download_started` / `download_completed` (bytes, durationMs), `zip_generated` / `zip_failed`.

## Links

- Feature: `TRF-003-recipient-page.md` (FR-003-3, FR-003-4, AC-003-1)
- Related: US-004-01 (generation), US-004-02 (caching), US-004-03 (cap note)
- Architecture: TA-4.2#6–7, TA-6.6, TA-7.2
- Design: UI-Reference §5.3
- Milestone: T-016, T-017
