# US-004-03 — See why "Download all" is unavailable

**Feature:** F-TRF-004 — Download All (ZIP) | **Status:** pending

---

**Story:** As a recipient of very large files, I want an explanation when "Download all" is missing, so that I don't think the feature is broken.
**Actor:** Recipient of a transfer whose total size exceeds `MAX_ZIP_SIZE`, or that has a single file.
**Goal:** Understand that per-file download is the intended path in this case.

## Preconditions

- Transfer is active.

## Happy path

1. Metadata (`GET /public/transfers/{linkId}`) carries `hasDownloadAll: false` when: file count = 1 **or** `SUM(SizeBytes) > MAX_ZIP_SIZE`.
2. The page renders per-file **Download** buttons only, plus — for the cap case — an inline note: "Files are large — download individually."
3. Per-file downloads work as usual (F-TRF-003, US-003-03).

## Alternative flows

- **Exactly at the cap** (sum == `MAX_ZIP_SIZE`): button is available (cap is "exceeds", not "equals").
- **Sender later re-sends with more files** (new transfer, new cap check): evaluation happens per transfer at send time.

## Acceptance criteria

```gherkin
Given a transfer of 6 GB total (cap 4 GB)
When I open the page
Then "Download all" is not shown
And a note says the files are large and I should download them individually
And per-file Download buttons work

Given a single-file transfer
When I open the page
Then "Download all" is not shown and no cap note appears
```

## Edge cases

- The cap is checked **before** generation starts (413 at the function, TA-6.6) — double safety, the UI should make the API case unreachable.
- Limit values come from the plan's `LimitsRecord` (TA-3.4), never literals.

## UI notes

- Note: `--fs-small`, `--fg-muted`, under the file list (UI-Reference §5.3).
- No disabled greyed-out button — absence + note is the pattern.

## Technical notes

- `hasDownloadAll` computed in `GetPublicTransferQuery` (TA-4.2a) from `FileCount` and `TotalBytes` vs `ILimitsProvider` value.
- Cap enforcement at `f-zip`: sum check → 413 (mapped by endpoint 7).

## Links

- Feature: `TRF-004-download-zip.md` (FR-004-4, FR-004-1)
- Plan AC: AC-004-2
- Architecture: TA-6.6, TA-3.4, TA-4.2a
- Design: UI-Reference §5.3
- Milestone: T-017
