# F-ALB-004 — Mobile Album View

**Priority:** P1 (Phase 2c) | **Phase:** 2c — Albums
**Spec source:** `02-feature-plan.md` F-ALB-004 (outline → remapped below as FR-037-*) | **Architecture:** TA-8.4, F-TRF-017
**Milestone tasks:** T-055 (M5)

---

## Description

The album is a **mobile product** for its main audience (phone-first photo sharing). **Swipeable grid**, **full-bleed image view**, **download** — all touch-first, all within the F-TRF-017 mobile-web budget. No native app; this is the mobile *web* surface, and it's the surface where the product meets its biggest audience.

**Actors:** viewer on a phone, contributor on a phone, owner on a phone.
**Value:** the audience that opens an album link is on a phone; if the grid is a desktop page crammed into a viewport, the product fails its first impression.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-037-1 | **Swipeable grid**: horizontal swipe (or vertical scroll, both) through items; swipe on an item opens the lightbox (swipe left/right = prev/next). Touch targets ≥ 44×44 px (F-TRF-017-4, UI-Reference §3). |
| FR-037-2 | **Full-bleed image view**: lightbox covers 100 dvh, image `object-fit: contain` on a `--bg` backdrop; swipe up to dismiss, left/right to navigate; download button always visible (≥ 44 px, `--bg-elevated` at 0.8 alpha). |
| FR-037-3 | **Download on mobile**: original file via 30-min SAS (TA-3.6); iOS Safari `Content-Disposition` fallback (F-TRF-003-10 / F-TRF-017 pattern) — "opens in Files/Photos on iOS, downloads on Android" is the expected split, documented. |
| FR-037-4 | Mobile-only: single column grid (F-ALB-003), sticky album title bar, pull-to-refresh off (avoid the infinite-scroll ambiguity). |
| FR-037-5 | Performance (TA-8.4 budget): grid TTI < 1.5 s on 4G with 50 items (thumbnails via a 300 px SAS variant — `?sv=...` with a transform or a thumb blob; D-23 decision: thumb blob generated on upload, proposed). |
| FR-037-6 | Video items: inline `<video controls playsinline>` in the lightbox; grid shows a static poster (first frame, generated on upload — D-23). |

## Acceptance criteria

```gherkin
AC-037-1: A user on a phone opens an album with 50 images
  Then the grid renders within 1.5 s (4G, p95) and scrolls/swipes smoothly

AC-037-2: A user swipes an image
  Then the full-bleed view opens and swipes navigate prev/next

AC-037-3: A user taps download on iOS Safari
  Then the file opens in Files/Photos (the documented iOS behavior)

AC-037-4: A user taps download on Android
  Then the file downloads to the device

AC-037-5: A video item is opened
  Then it plays inline with controls in the lightbox
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-037-1 | Thumbnail missing (generation failed) | Grid shows the file icon + name (graceful), full image still loads in the lightbox |
| EC-037-2 | Very large image (> 20 MP) | Lightbox loads the full-size via the SAS (no client downscale in MVP — D-23; a responsive `<img srcset>` is an optimization) |
| EC-037-3 | iOS zoom gesture in the lightbox | Native pinch-zoom allowed (the lightbox opts out of `touch-action: none` on the image only) |
| EC-037-4 | Mobile contributor upload | Same UploadEngine path (F-TRF-017-1) — the mobile view is the consumer of it, no separate upload UI |

## UI notes (UI-Reference §3, F-TRF-017)

- Title bar: sticky, `--bg` at 0.95 alpha, album title + (owner) a "New" upload button.
- Lightbox caption: file name + size, `--fs-small`, below the image (not overlaid — touch-friendly).
- No hover states (touch); focus rings for keyboard users preserved (F-TRF-016-1).

## Technical notes

- Thumbnails: proposed `albums/{albumId}/thumbs/{fileId}` blob, 300 px, generated on upload (Function `f-thumb` or inline — D-23; the pattern follows F-SGN-004's function decision).
- Grid: CSS grid 1-col mobile / 2-col tablet / 3-col desktop (F-ALB-003); virtualization **not** in MVP (50-item budget is met without it — measured, TA-15).
- Lightbox: `position: fixed; inset: 0; height: 100dvh` (mobile Safari-safe); `role=dialog`, `aria-modal`, focus trap (F-TRF-016-2).
- Telemetry (TA-10.2 extension): `album_opened { albumId, isMobile }` (PII-safe boolean).

## Test plan

- Unit: thumb URL construction; lightbox index wrap-around (1..N, circular).
- Integration: SAS minting for thumb + full (two URLs per item).
- E2E (Playwright mobile viewport, iOS + Android profiles): AC-037-1…037-5 (swipe via `touchscreen` API, download assertion per platform, video `playsinline`).
- Load: 50-item album, 4G throttle, TTI p95 < 1.5 s (TA-15 gate).

## User stories

| ID | Story | File |
|---|---|---|
| US-037-01 | Browse my album on a phone | `US-037-01-mobile-grid.md` |
| US-037-02 | See a photo full-screen on a phone | `US-037-02-mobile-lightbox.md` |
| US-037-03 | Download a photo from my phone | `US-037-03-mobile-download.md` |
