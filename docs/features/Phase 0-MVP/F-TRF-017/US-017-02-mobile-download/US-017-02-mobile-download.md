# US-017-02 — Download files on my phone

**Feature:** F-TRF-017 — Mobile Web | **Status:** pending

---

**Story:** As a recipient on a phone, I want to download the files (single or zip) straight to my device, so that the file arrives in my hand — not on a laptop I don't have.
**Actor:** Recipient on mobile Safari (iOS) or Chrome (Android), on an active transfer link.
**Goal:** single files download with the original filename; **Download all** (zip) triggers the native download bar on iOS; per-file fallback always present.

## Preconditions

- An active transfer link opened on a phone (US-003-06 covers the page layout; this story covers the *download mechanics* on mobile).

## Happy path

1. Recipient opens the link on their phone; the page is single-column with a sticky bottom **Download** / **Download all** button (FR-017-5).
2. Tapping a per-file **Download** downloads that file with its **original filename** (FR-017-3).
3. Tapping **Download all** (≥2 files) builds the zip (F-TRF-004) and **Safari shows the zip in its visible download bar** (FR-017-3).
4. The extracted files keep their original names (zip entries are top-level original names, FR-004-5).
5. "Downloads left" decrements (US-003-05); the growth-loop CTA appears after a full download (US-003-07).

## Alternative flows

- **Android Chrome:** the zip downloads to the **Downloads** folder (EC-017-4); per-file buttons remain the documented fallback.
- **4G flaky download:** browser-native Range resume against the SAS URL works on mobile too (EC-017-2, EC-003-1).
- **Single-file transfer:** only the per-file **Download** is shown (no **Download all**, FR-004-1).

## Acceptance criteria

```gherkin
  When they open a 2-file transfer
  Then they can "Download all"
  And Safari shows the zip download
  And the original file names survive the extraction

Given a per-file download on iOS
When it completes
Then the file is saved with its original filename

Given an Android Chrome download of a zip
When it completes
Then the file is in the Downloads folder (per-file buttons remain the fallback)
```

## Edge cases

- **iOS cancels the zip download:** the per-file buttons remain the fallback (EC-004-4); no error state left behind.
- **Large zip near `MAX_ZIP_SIZE`:** the button is hidden with the inline note if over the cap (US-004-03) — on a phone the note is readable and per-file download is the path.
- **No horizontal scroll at 360 px** and all touch targets ≥ 44 px (FR-017-3/4, AC-017-3).

## UI notes

- Recipient page: sticky bottom **Download** (and **Download all** when applicable); the page scrolls under it (UI-Reference §5.3).
- Generating state: button → "Preparing your download…" (spinner, disabled) — same as desktop (F-TRF-004).
- Inputs 44 px tall, 16 px font (FR-017-4).

## Technical notes

- Delivery is server-identical to desktop (30-min read SAS on the file/`all.zip`); the mobile difference is the native download-bar/Downloads-folder surface (FR-017-3).
- E2E: Playwright mobile context (iPhone profile) asserts the zip download where the browser exposes it (F-TRF-017 test plan, T-027).
- Manual: real-device spot check (Safari + Chrome) in staging (T-027).

## Links

- Feature: `TRF-017-mobile.md` (FR-017-3, AC-017-2, EC-017-2/4)
- Related: US-003-02 (download-all), US-003-03 (per-file), US-004-01 (zip generation)
- Architecture: TA-8.4, TA-15
- Design: UI-Reference §3, §5.3
- Milestone: T-027
