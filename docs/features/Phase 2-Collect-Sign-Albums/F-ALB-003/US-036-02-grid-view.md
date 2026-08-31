# US-036-02 — Browse the album as a grid

**Feature:** F-ALB-003 — Contribute to an Album | **Status:** pending

---

**Story:** As a viewer (or the owner), I want the album as a browsable grid of items, so that the collection reads at a glance — cover first, everything else in order.
**Actor:** viewer (guest or owner), on desktop or tablet.
**Goal:** the album grid — equal-sized tiles, cover pinned top-left with a "Cover" chip, `--bg-subtle` gaps, `--radius-sm` thumbs (FR-036-3).

## Preconditions

- An album with ≥ 1 item (or none — the empty state).

## Happy path

1. Open `{origin}/album/{linkId}` → the grid renders: cover first (pinned top-left, "Cover" chip), then items in `SortOrder` (upload order, FR-036-3).
2. Tiles are equal-sized, 3 columns on desktop, 2 on tablet (1 on phone — F-ALB-004), thumbs via 300 px thumbnail blobs (F-ALB-004 FR-037-5 pattern).
3. Click a tile → the lightbox (US-036-03). Hover (desktop only): slight elevation, no hover *state* on mobile (touch).

## Alternative flows

- **Zero items** (EC-034-1): "No photos yet." + (owner) the upload drop zone — the grid's empty state is where new albums live.
- **Cover deleted** (EC-034-3): falls back to the first item — no orphan cover, no "missing cover" UI.
- **Non-image item** (EC-036-1): file icon + name tile — the grid is honest about mixed content.

## Acceptance criteria

```gherkin
Given an album with a cover and 10 items
When I open it
Then the cover is top-left with the Cover chip and the remaining items follow in upload order

Given an album with no items
When I open it
Then the empty state shows (and, as owner, the drop zone)
```

## Edge cases

- No virtualization in MVP: the 50-item mobile budget is met without it (F-ALB-004 FR-037-5, measured per TA-15); desktop collections can be larger — the cap is the album size, not the item count (documented).
- Tiles are `--bg-subtle` separated, no borders — the gap IS the separator (UI-Reference §3/§5).

## UI notes

- Grid: `grid-template-columns: repeat(3, 1fr)` desktop / 2 tablet / 1 phone; thumbs `aspect-ratio: 1/1`, `object-fit: cover`.
- Tile caption: none on the tile (name is in the lightbox — tiles are the *look*, not the list).

## Technical notes

- `GET /api/v1/albums/{linkId}/items?cursor=` (the album read endpoint; TA-4.2 extension) → cursor-paginated `AlbumItem` list with thumb + full SAS URLs per item (two URLs, TA-3.6).
- Cover pin: `CoverBlobRefId` first, then `SortOrder`; a "set cover" row action is owner-only (D-23).
- Thumbnails: `albums/{albumId}/thumbs/{fileId}` 300 px, generated on upload (D-23, F-ALB-004 technical notes).

## Links

- Feature: `ALB-003-contribute-album.md` (FR-036-3)
- Related: US-036-03 (tile → lightbox), F-ALB-004 (the mobile grid), US-034-01 (cover semantics)
