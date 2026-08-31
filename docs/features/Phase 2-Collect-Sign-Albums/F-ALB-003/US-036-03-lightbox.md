# US-036-03 — See one image full-bleed

**Feature:** F-ALB-003 — Contribute to an Album | **Status:** pending

---

**Story:** As a viewer, I want to open any item full-bleed — with prev/next and a download — so that I can actually *see* what I'm looking at, and take it.
**Actor:** viewer (guest or owner), desktop or mobile.
**Goal:** the lightbox — full-bleed image (or video with `controls`), prev/next, download button, ESC/arrows, `role=dialog`, focus trap (FR-036-4, F-TRF-016).

## Preconditions

- A grid with ≥ 1 item (US-036-02).

## Happy path

1. Click a tile → lightbox: full-bleed image (`object-fit: contain` on `--bg`), caption bar below with file name + size, **Download** button (30-minute SAS, TA-3.6).
2. **← / →** (or on-screen prev/next buttons) navigate the album in order, wrap-around (1..N circular).
3. **ESC** (or the close affordance) dismisses; focus returns to the originating tile (F-TRF-016 focus management).
4. Video item: inline `<video controls playsinline>` in the lightbox (F-ALB-004 FR-037-6, AC-037-5).

## Alternative flows

- **Non-image, non-video item** (a doc): the lightbox shows the file card (icon + name + size + **Download**) — no fake preview (EC-036-1 honesty).
- **Mobile**: the same lightbox is the F-ALB-004 surface (swipe nav, 100 dvh — US-037-02).

## Acceptance criteria

```gherkin
Given I open an image in the lightbox
When I press the right arrow
Then the next item shows (and from the last, it wraps to the first)

Given I press Escape
When the lightbox closes
Then focus returns to the tile I opened
```

## Edge cases

- Missing thumbnail (EC-037-1): grid shows file icon + name; the lightbox loads the full-size image anyway (the thumb is only the grid's concern).
- Very large image (> 20 MP): full-size via SAS, no client downscale in MVP (EC-037-2, D-23).

## UI notes

- Lightbox backdrop: `--bg` at 0.9 alpha; caption `--fs-small`, below the image (touch-friendly, not overlaid).
- Focus trap + `aria-modal` (F-TRF-016-2); prev/next buttons ≥ 44 px (F-TRF-017-4) and reachable by keyboard.

## Technical notes

- Lightbox state is client-side (the item list is already fetched / cursor-fetched ahead by ±1 — P2 nicety: prefetch next, MVP: fetch on demand).
- Download: per-item 30-min SAS minted on demand (`GET …/files/{fileId}/download-url` pattern, TA-3.6).
- Telemetry `album_item_viewed { albumId, isVideo }` (PII-safe).

## Links

- Feature: `ALB-003-contribute-album.md` (FR-036-4)
- Related: F-ALB-004 (the mobile form of this lightbox), US-036-02 (the entry point), F-TRF-016 (the a11y contract)
