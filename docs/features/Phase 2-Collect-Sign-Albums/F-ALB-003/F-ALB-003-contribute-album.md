# F-ALB-003 — Contribute to an Album

**Priority:** P1 (Phase 2c) | **Phase:** 2c — Albums
**Spec source:** `02-feature-plan.md` F-ALB-003 (outline → remapped below as FR-036-*) | **Architecture:** TA-8.3, TA-3.5, TA-4.2
**Milestone tasks:** T-054 (M5)

---

## Description

The owner can **invite contributors** (by email) who upload **to** the album — and the owner can always upload. The album is a **grid** of items with a **lightbox** (full-bleed view + prev/next + download). Contributions are visible immediately (no approval queue in MVP — D-23 decision: approval is Phase 3). The owner sees contributors; contributors see the album (not the contributor list, MVP).

**Actors:** owner (uploads + invites), contributor (guest with a contributor link), viewer.
**Value:** multiple hands on one gallery — the "team moodboard" and "client + photographer both add" cases.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-036-1 | Owner + **invited contributors** (by email) upload. Each contributor gets a **contributor link** (`{origin}/album/{linkId}/contribute/{token}`) — token is single-use per session (re-issueable from the album screen, old tokens invalidated — documented). |
| FR-036-2 | Upload: the shared `UploadEngine` (TA-8.3) → `POST /api/v1/albums/{id}/contributions/draft` (42) → SAS → `…/contributions/draft/{draftId}/finalize` (43) → `AlbumItem` rows (BlobRef shared). |
| FR-036-3 | **Grid view** (owner + viewer): masonry-free equal grid (3-col desktop, 2-col tablet, 1-col phone), cover first (if set, pinned top-left with a "Cover" chip). |
| FR-036-4 | **Lightbox**: full-bleed image (or video, `controls`), prev/next, download button, ESC/arrow keys (F-TRF-016), `role=dialog`. |
| FR-036-5 | Contributor identity: optional "Your name" at first contribution (stored per (album, token) — MVP: a `ContributorToken` row maps token → name; no account). |
| FR-036-6 | Album close (owner): `Status = Closed` — no more uploads; grid still viewable; a "Closed" chip. Re-open allowed. |

## Acceptance criteria

```gherkin
AC-036-1: A contributor uploads to an album
  Then the items appear in the grid for the owner and viewers immediately

AC-036-2: The owner uploads
  Then the items appear (same path, owner token implicit)

AC-036-3: A viewer opens the lightbox
  Then full-bleed view with prev/next and download works

AC-036-4: The owner closes the album
  Then contributors' upload links show "This album is closed." and the grid is read-only

AC-036-5: A contributor with a re-issued token uses the old token
  Then the old token is invalidated (new token only, documented)
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-036-1 | Non-image upload (a doc) | Accepted, shown with a file icon in the grid (albums are "photos first", not "photos only" — documented) |
| EC-036-2 | Contributor and owner upload same file name | Both kept (no de-dup on the album — it's a gallery, not a transfer; documented) |
| EC-036-3 | Album full (per-album cap) | Upload rejected with the cap named (F-ALB-001-5) |
| EC-036-4 | Contributor token expires (revoked) | "Your contributor link has been updated — ask for a new one." state |

## UI notes (UI-Reference §5.3 pattern)

- Grid: `--bg-subtle` gaps, `--radius-sm` on thumbs; lightbox `--bg` at 0.9 alpha, white caption bar with file name + **Download**.
- Contributor upload screen: drop zone + file rows (4.2) + overall progress — identical to the landing page (F-TRF-001).
- Cover pin: first item with a "Cover" chip; owner can set a specific item as cover (right-click / row action, D-23).

## Technical notes

- `AlbumItem` (TA-3.2 addition, migration): `Id, AlbumId, BlobRefId, OriginalName, SizeBytes, ContentType, ContributorName?, SortOrder, CreatedAtUtc`. (Reuses the `FileItem`-generalization idea from F-COL-002: `FileItem.AlbumId` nullable — *proposed* unification, D-23; if not, `AlbumItem` is a separate table. The spec freezes `AlbumItem` separate for clarity; unification is an optimization.)
- `ContributorToken` (TA-3.2 addition): `Id, AlbumId, Name?, ExpiresAtUtc?, RevokedAtUtc?`.
- Blob layout (TA-3.5 addition): `albums/{albumId}/files/{fileId}`.
- Events (TA-5.3 extension): `album.item_added { albumId, fileId, contributor? }`.
- Telemetry (TA-10.2 extension): `album_uploaded`.

## Test plan

- Unit: token revocation logic; grid ordering (cover pin, SortOrder); lightbox index math.
- Integration: AC-036-1…036-5 (contributor upload → item row; owner upload; close state; token invalidation).
- E2E: contributor uploads 2 images → grid shows them → lightbox nav (Playwright).

## User stories

| ID | Story | File |
|---|---|---|
| US-036-01 | Let someone else add to my album | `US-036-01-invite-contributor.md` |
| US-036-02 | Browse the album as a grid | `US-036-02-grid-view.md` |
| US-036-03 | See one image full-bleed | `US-036-03-lightbox.md` |
| US-036-04 | Close my album to new uploads | `US-036-04-close-album.md` |
