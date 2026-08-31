# F-TRF-017 — Mobile Web

**Priority:** P1 | **Phase:** 0 — Core Transfer
**Spec source:** `02-feature-plan.md` F-TRF-017 | **Architecture:** TA-8.4, TA-14.1 (E2E matrix), UI-Reference §3
**Milestone tasks:** T-027

---

## Description

The recipient page is where phones matter most (the link arrives in an SMS, a chat, a pocket). All P0 screens work in **mobile Safari and Chrome** without desktop emulation: uploads use the native file input (camera/Photos on iOS), downloads trigger the native download bar (zips included), and every touch target is ≥ 44 px.

**Actors:** any user on a phone (recipients overwhelmingly, senders secondarily).
**Value:** "works on a phone" is a trust feature — the file arrives in the hand, not in a laptop.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-017-1 | All P0 screens usable in mobile Safari/Chrome without desktop emulation (landing, link, recipient incl. password/download states, auth, My Files). |
| FR-017-2 | **Upload on mobile:** `<input type=file>` (camera/Photos on iOS); drag-and-drop not required; multi-select (`multiple`) where the OS allows. |
| FR-017-3 | **Download on iOS:** single files download with the original filename; **zip** triggers Safari's visible download bar (F-TRF-004; per-file fallback always available). |
| FR-017-4 | **Touch targets:** all interactive elements ≥ 44 × 44 px (UI-Reference §3). |
| FR-017-5 | **Layout:** single column, sticky bottom **Download** button on the recipient page (F-TRF-003-10); sticky bottom **Send.** on the upload surface; no hover-dependent affordances (no tooltips as the only channel). |

## Acceptance criteria

```gherkin
AC-017-1: A guest on an iPhone
  When they open the landing page
  Then they can select 2 photos from Photos
  And upload completes (progress visible)
  And they get a link and can copy it

AC-017-2: A recipient on a phone
  When they open a 2-file transfer
  Then they can "Download all"
  And Safari shows the zip download
  And the original file names survive the extraction

AC-017-3: Any P0 screen on a phone
  When inspected
  Then no horizontal scroll at 360 px width
  And all touch targets are ≥ 44 px
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-017-1 | iOS paste upload | Not required (clipboard paste is desktop-first); if an image is pasted via the iOS share sheet into the file input, it works as a normal file |
| EC-017-2 | 4G flaky download | Range-resume on the SAS URL (F-TRF-003, EC-003-1) — browser-native, works on mobile too |
| EC-017-3 | Zoom | Text selectable/scaleable (no `user-scalable=no`); layout survives 200% zoom (WCAG 1.4.4) |
| EC-017-4 | Android Chrome zip | Downloads to the Downloads folder; per-file buttons remain the documented fallback |

## UI notes (UI-Reference §3, §5.3)

- Breakpoint behavior: ≤ 640 px → single column everywhere; top bar collapses to logo + avatar; file rows keep their 44 px min height.
- Recipient page: sticky bottom **Download** (and **Download all** when applicable) — the page scrolls under it (UI-Reference §5.3).
- Upload surface: sticky bottom **Send.** with the overall progress line above it (US-001-04 state, mobile layout).
- Forms: inputs 44 px tall, 16 px font (prevents iOS zoom-on-focus).

## Technical notes

- E2E matrix (T-027 exit "mobile matrix green in staging"): Playwright mobile contexts (iPhone 14 Safari profile, Pixel Chrome profile) over the guest loop, password flow, and download-all.
- Performance: TA-8.4 budget applies on mobile (JS < 300 KB gz; TTI < 1 s on 4G for `/` and `/t/{linkId}`).
- `viewport` meta + `apple-mobile-web-app-capable` not set (not a PWA in MVP — documented).

## Test plan

- E2E (Playwright, mobile contexts): AC-017-1 (select-from-Photos simulated via file input), AC-017-2 (zip download assertion where the browser exposes it), AC-017-3 (360 px viewport: no horizontal scroll via `document.scrollWidth <= innerWidth`; 44 px targets via computed styles).
- Manual: real-device spot check in staging (Safari + Chrome), part of T-027.

## User stories

| ID | Story | File |
|---|---|---|
| US-017-01 | Send files from my phone | `US-017-01-mobile-upload.md` |
| US-017-02 | Download files on my phone | `US-017-02-mobile-download.md` |
| US-017-03 | Tap everything comfortably | `US-017-03-touch-ui.md` |
