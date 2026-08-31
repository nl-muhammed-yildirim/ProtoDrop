# US-037-02 — See a photo full-screen on a phone

**Feature:** F-ALB-004 — Mobile Album View | **Status:** pending

---

**Story:** As a viewer on a phone, I want to open a photo full-screen and swipe through the album, so that I can see it properly without pinching or zooming.
**Actor:** viewer on a phone.
**Goal:** the full-bleed lightbox — 100 dvh, `object-fit: contain` on `--bg`; swipe left/right = prev/next; swipe up = dismiss; download always visible (FR-037-2, AC-037-2).

## Preconditions

- A mobile grid (US-037-01) with items.

## Happy path

1. Swipe up (or tap) on a tile → lightbox covers 100 dvh; the image is `contain`-centered on `--bg`.
2. **Swipe left/right**: prev/next, wrap-around (the same circular order as desktop, US-036-03).
3. **Swipe up**: dismiss, back to the grid (position preserved).
4. **Download** button always visible — ≥ 44 px, `--bg-elevated` at 0.8 alpha (FR-037-2) — plus the caption (file name + size, `--fs-small`, below the image).

## Alternative flows

- **Video item** (AC-037-5, FR-037-6): `<video controls playsinline>` in the lightbox; the grid tile is a static poster (first frame, generated on upload — D-23).
- **Pinch-zoom** (EC-037-3): native pinch-zoom allowed on the image only — the lightbox opts out of `touch-action: none` for the image element (the swipe gestures still own the backdrop).

## Acceptance criteria

```gherkin
Given I open a photo full-screen
When I swipe left
Then the next photo shows (and from the last, the first)

Given I'm in the lightbox
When I swipe up
Then the lightbox dismisses and the grid is where I left it

Given a video item
When I open it
Then it plays inline with controls
```

## Edge cases

- Very large image (> 20 MP, EC-037-2): full-size via SAS, no client downscale in MVP (D-23) — the budget is measured at normal photo sizes (TA-15).
- Gesture conflict (scroll vs dismiss): the image is `contain` with its own scroll area; the backdrop owns horizontal swipes (documented gesture map).

## UI notes

- No hover; all affordances ≥ 44×44 px (F-TRF-017-4).
- The download button is the *only* control overlaid near the image; the caption sits below (F-ALB-004 UI notes).

## Technical notes

- `position: fixed; inset: 0; height: 100dvh` (mobile Safari-safe); `role=dialog`, `aria-modal`, focus trap (F-TRF-016-2 — keyboard users keep full access).
- Navigation state is client-side; full-size SAS minted per opened item (TA-3.6).
- E2E: Playwright mobile viewport (iOS + Android profiles), `touchscreen` API for swipes (F-ALB-004 test plan).

## Links

- Feature: `ALB-004-mobile-album.md` (FR-037-2/6, AC-037-2/5, EC-037-2/3)
- Related: US-036-03 (the desktop lightbox this mirrors), US-037-01 (the entry point), F-TRF-016 (dialog semantics)
