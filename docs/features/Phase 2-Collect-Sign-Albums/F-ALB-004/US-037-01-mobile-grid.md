# US-037-01 — Browse my album on a phone

**Feature:** F-ALB-004 — Mobile Album View | **Status:** pending

---

**Story:** As someone opening the album link on a phone, I want a grid that is fast and swipeable, so that the album feels like a photo app — not a desktop page crammed into a viewport.
**Actor:** viewer on a phone (the album's main audience).
**Goal:** the mobile grid — single column, sticky title bar, thumbnails from 300 px blobs, TTI < 1.5 s on 4G with 50 items (FR-037-1/5, AC-037-1).

## Preconditions

- An album with items (US-034-01, F-ALB-003 grid).

## Happy path

1. Open `{origin}/album/{linkId}` on a phone → sticky title bar (album title + (owner) a "New" upload button), then the single-column grid.
2. Tiles use 300 px thumbnail blobs (generated on upload, D-23) — the grid renders within the TA-8.4 budget; scroll is smooth, touch targets ≥ 44×44 px (F-TRF-017-4).
3. Swipe up on a tile (or tap) → the full-bleed lightbox (US-037-02).
4. No hover states (touch); keyboard focus rings preserved (F-TRF-016-1); no pull-to-refresh (avoids infinite-scroll ambiguity, FR-037-4).

## Alternative flows

- **Thumbnail missing** (generation failed, EC-037-1): the tile shows the file icon + name — graceful; the full image still loads in the lightbox.
- **Owner on a phone**: the same grid + the "New" button (F-TRF-017 upload path — F-ALB-003/FR-037-4, no separate upload UI).

## Acceptance criteria

```gherkin
Given an album with 50 images
When I open it on a phone (4G)
Then the grid renders within 1.5 s (p95) and scrolls/swipes smoothly

Given a tile's thumbnail failed to generate
When the grid renders
Then that tile shows the file icon and name, and the lightbox loads the full image
```

## Edge cases

- No virtualization in MVP — the 50-item budget is met without it (FR-037-5, measured per TA-15); larger albums are a load-test gate, not a feature.
- Sticky title bar: `--bg` at 0.95 alpha — the grid scrolls under it (the bar is the only fixed element).

## UI notes

- Single column (F-ALB-003 grid on phone = 1 col); tiles full-width, `aspect-ratio: 1/1`, `object-fit: cover`.
- Lightbox caption below the image (not overlaid — touch-friendly, F-ALB-004 UI notes).

## Technical notes

- Thumbnails: `albums/{albumId}/thumbs/{fileId}` 300 px blob, generated on upload (D-23 — `f-thumb` function or inline; the pattern follows F-SGN-004's function decision).
- Two URLs per item (thumb + full, TA-3.6 minting) — the grid requests thumbs only.
- Telemetry `album_opened { albumId, isMobile }` (PII-safe boolean).
- Load gate: 50-item album, 4G throttle, TTI p95 < 1.5 s (TA-15).

## Links

- Feature: `ALB-004-mobile-album.md` (FR-037-1/4/5, AC-037-1, EC-037-1)
- Related: US-036-02 (the desktop grid this is the mobile form of), F-TRF-017 (the mobile budget), F-ALB-003 (thumbnails & tokens)
