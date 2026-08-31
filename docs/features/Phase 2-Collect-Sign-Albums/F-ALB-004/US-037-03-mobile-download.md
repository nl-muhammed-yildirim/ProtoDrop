# US-037-03 — Download a photo from my phone

**Feature:** F-ALB-004 — Mobile Album View | **Status:** pending

---

**Story:** As a viewer on a phone, I want to download a photo to my device, so that it lands in my Photos/Files where I can share it — not in a limbo tab.
**Actor:** viewer on a phone (iOS Safari or Android Chrome).
**Goal:** download via the 30-minute SAS (TA-3.6) with the documented platform split — iOS Safari opens the file in Files/Photos, Android downloads it to the device (FR-037-3, AC-037-3/4).

## Preconditions

- A lightbox (US-037-02) with an item open, or the mobile grid.

## Happy path

1. Tap **Download** (the always-visible button, ≥ 44 px) → the API mints a 30-minute SAS for the original file → the browser performs the platform download.
2. **iOS Safari**: `Content-Disposition` fallback (F-TRF-003-10 / F-TRF-017 pattern) — the file opens in Files/Photos (AC-037-3, the documented iOS behavior).
3. **Android**: the file downloads to the device's download folder (AC-037-4).
4. The original file is served (not the thumbnail) — the download is the asset, not a preview.

## Alternative flows

- **Video item**: the download is the video file (the lightbox's **Download** acts on the item, FR-037-3 — no separate "save video" path in MVP).
- **Non-image doc** (EC-036-1 family): same download; the file card's button, not a lightbox.

## Acceptance criteria

```gherkin
Given I tap Download on iOS Safari
When the action completes
Then the file opens in Files/Photos (the documented iOS split)

Given I tap Download on Android
When the action completes
Then the file downloads to the device
```

## Edge cases

- SAS expiry mid-download: 30 minutes is generous; a mid-download 404 renders the standard error screen with **Retry** (the F-TRF-013 error vocabulary — no special mobile error).
- Large file (> 100 MB) on a metered connection: the download proceeds (the button says "Download", the size is in the caption — informed, not gated; documented).

## UI notes

- The button label is always **Download** — the platform does the rest; no "Save to Photos" / "Download file" label split (one label, two honest outcomes, documented).
- After success: a transient "Downloaded" state on the button (no toast stack — one line, `--fs-small`).

## Technical notes

- SAS minting: `GET /api/v1/albums/{linkId}/files/{fileId}/download-url` → 30-min read SAS (TA-3.6); iOS `Content-Disposition: inline` vs `attachment` behavior per F-TRF-003-10.
- No `fetch`-and-blob in MVP (memory on phones); the browser's native handler owns it (documented).
- Telemetry `album_download { albumId, platform }` — `platform` is a coarse (ios|android|other) boolean-family value, PII-safe.

## Links

- Feature: `ALB-004-mobile-album.md` (FR-037-3, AC-037-3/4)
- Related: F-TRF-003 (the SAS + Content-Disposition machinery), F-TRF-017 (the mobile contract), US-037-02 (the button lives here)
