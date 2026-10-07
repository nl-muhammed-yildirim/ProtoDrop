# US-004-01 — Generate a zip of the whole transfer

**Feature:** F-TRF-004 — Download All (ZIP) | **Status:** pending

---

**Story:** As a recipient who wants everything at once, I want "Download all" to give me one zip with every file, so that I don't download files one by one and lose track of what I got.
**Actor:** Recipient (no account) on the page of a transfer with ≥2 files.
**Goal:** One click → one zip containing the whole transfer, with original file names.

## Preconditions

- Transfer is `Active`, has ≥2 files, and total size ≤ `MAX_ZIP_SIZE`.
- The recipient page shows per-file **Download** plus **Download all** (FR-004-1).

## Happy path

1. Recipient presses **Download all**.
2. The API answers 202 (zip not ready yet) and the button becomes "Preparing your download…".
3. `f-zip` streams the files (ordered by `SortOrder`) into `transfers/{id}/all.zip` and mints a 30-min SAS.
4. The client polls every 2 s; on 200 it triggers the browser download of the zip.
5. The recipient gets a zip whose entries are the original file names, top-level, no folders.

## Alternative flows

- **Zip already cached:** endpoint returns 200 immediately (US-004-02).
- **Preparation fails after 3 minutes of polling:** button reverts to idle, error toast "Download preparation failed — try again".
- **Recipient cancels the browser download:** nothing is broken — the cached zip remains for the next attempt.

## Acceptance criteria

```gherkin
Given a 3-file transfer that is active
When I press "Download all"
Then the button shows the preparing state
And after polling I receive a zip
And the zip contains all 3 files with their original names

Given two files named "report.pdf" and "report.pdf"
When the zip is generated
Then the zip contains "report.pdf" and "report_1.pdf"
And both file contents are intact
```

## Edge cases

- Generation streams with a 4 MB buffer — a 4 GB zip never fills the function's memory (EC-004-1).
- If the transfer expires mid-generation, the partial zip is deleted; the next click regenerates (EC-004-2).

## UI notes

- Button states: idle → "Preparing your download…" (spinner) → download starts → idle.
- Polling line under the button in `--fs-small`: "This can take a minute for large transfers."

## Technical notes

- `GET /api/v1/public/transfers/{linkId}/download-all-url?t=` → 200 `{url}` / 202 `{pollAfterSec:2}` (TA-4.2#7, TA-7.2).
- Generation: `f-zip`, `ZipArchive` streaming mode, entries de-duped `_1`/`_2` (TA-6.6).
- `download.completed` emitted with `fileId=null`; the download counts against `MaxDownloads` like any download (TA-7.2).

## Links

- Feature: `TRF-004-download-zip.md` (FR-004-1, FR-004-2, FR-004-5, FR-004-6)
- Plan AC: AC-004-1
- Architecture: TA-6.6, TA-4.2#7, TA-7.2
- Design: UI-Reference §5.3
- Milestone: T-017
