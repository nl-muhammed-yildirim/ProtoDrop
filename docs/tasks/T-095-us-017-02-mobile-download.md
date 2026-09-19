# T-095 — Download files on my phone

**Story:** US-017-02 | **Feature:** F-TRF-017 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-017/US-017-02-mobile-download.md`
**Coarse task (Milestone-Backlog.md):** T-027
**Status:** pending

---

## Scope

As a recipient on a phone, I want to download the files (single or zip) straight to my device, so that the file arrives in my hand — not on a laptop I don't have.

**Actor:** Recipient on mobile Safari (iOS) or Chrome (Android), on an active transfer link.

**Goal:** single files download with the original filename; **Download all** (zip) triggers the native download bar on iOS; per-file fallback always present.

Happy path:

1. Recipient opens the link on their phone; the page is single-column with a sticky bottom **Download** / **Download all** button (FR-017-5).
2. Tapping a per-file **Download** downloads that file with its **original filename** (FR-017-3).
3. Tapping **Download all** (≥2 files) builds the zip (F-TRF-004) and **Safari shows the zip in its visible download bar** (FR-017-3).
4. The extracted files keep their original names (zip entries are top-level original names, FR-004-5).
5. "Downloads left" decrements (US-003-05); the growth-loop CTA appears after a full download (US-003-07).

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

## Exit check

- [ ] Scenario 1: a per-file download on iOS
- [ ] Scenario 2: an Android Chrome download of a zip
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-017/US-017-02-mobile-download.md`
- Feature: `TRF-017-mobile.md` (FR-017-3, AC-017-2, EC-017-2/4)
- Related: US-003-02 (download-all), US-003-03 (per-file), US-004-01 (zip generation)
- Architecture: TA-8.4, TA-15
- Design: UI-Reference §3, §5.3
- Milestone: T-027
