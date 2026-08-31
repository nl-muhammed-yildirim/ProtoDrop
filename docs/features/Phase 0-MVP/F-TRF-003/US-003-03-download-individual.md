# US-003-03 — Download individual files

**Feature:** F-TRF-003 — Recipient Download Page | **Status:** pending

---

**Story:** As a recipient who only needs one of several files, I want to download a single file directly, so that I don't have to unpack a zip for one file.
**Actor:** Recipient (any device) on an active transfer.
**Goal:** Per-file **Download** streams the file with its original filename, via a time-boxed SAS.

## Preconditions

- The transfer is `Active`; the file still exists in storage (not yet physically deleted — US-005-02).

## Happy path

1. Recipient clicks **Download** on one file row.
2. The server mints a 30-minute read SAS (FR-003-3) and the browser streams the blob.
3. The file arrives with its **original filename** (no `.zip` wrapper, no renamed temp name).
4. `DownloadsCount` increments (MVP: counted at SAS mint); the row's state is unchanged — the file can be re-downloaded within the transfer's life.

## Alternative flows

- **Unicode / spaces / quotes in filenames:** original bytes preserved in `Content-Disposition` `filename*` (RFC 5987, EC-003-3).
- **Flaky network on a large file:** the browser resumes natively via Range requests against the SAS URL (Blob SAS supports Range, EC-003-1).
- **Storage degraded:** the mint fails → the row shows "File temporarily unavailable." while the rest of the page keeps working (US-013-02).

## Acceptance criteria

```gherkin
Given an active transfer with 5 files
When I click Download on one file
Then it downloads directly (not zipped) with its original filename
And the download URL is a time-boxed SAS (30 min)
And "Downloads left" decrements by 1 (when the transfer has a finite cap)

Given a file named "Q4 report (final) v2.pdf"
When I download it
Then the saved file keeps the exact original name, including spaces and parentheses
```

## Edge cases

- Re-downloading the same file: allowed any number of times until the transfer expires or the download cap is hit (counted per request — MVP over-count, documented, F-TRF-003 technical notes).
- 0-byte file: downloads fine, shows 0 B in the list.

## UI notes

- Per-file **Download** is a ghost button in the row's action column (UI-Reference §4.2, §5.3); `aria-label` "Download {name}, {size}" (F-TRF-016).
- During download: row shows a subtle progress/indicator; no page-level blocking UI.

## Technical notes

- Endpoint 6: mint SAS + increment `DownloadsCount` + `DownloadEvent` row + cap check (TA-4.2#6, TA-7.2) — the cap flip happens here (US-005-03).
- Telemetry: `download_started`, `download_completed` (bytes, durationMs, fileId).

## Links

- Feature: `TRF-003-recipient-page.md` (FR-003-1, FR-003-3, AC-003-1)
- Related: US-005-03 (cap), US-013-02 (degraded storage)
- Architecture: TA-4.2#6, TA-7.2, TA-3.6
- Design: UI-Reference §5.3
- Milestone: T-016
