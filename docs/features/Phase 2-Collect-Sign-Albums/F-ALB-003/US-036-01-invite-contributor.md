# US-036-01 — Let someone else add to my album

**Feature:** F-ALB-003 — Contribute to an Album | **Status:** pending

---

**Story:** As an album owner, I want to invite a contributor who can upload to my album, so that the gallery is built by more than my hands — a photographer and a client both adding, a team moodboard.
**Actor:** album owner; contributor (guest with a contributor link, no account).
**Goal:** contributor invitations by email → a **contributor link** `{origin}/album/{linkId}/contribute/{token}` → the contributor uploads through the shared `UploadEngine` (FR-036-1/2).

## Preconditions

- An album exists (US-034-01), status `Active`.

## Happy path

1. Album detail → **Add contributor** → email field → invite email with the contributor link (the `ContributorToken` row maps token → optional name).
2. Contributor opens the link → optional "Your name" (stored per (album, token), FR-036-5) → the landing-page drop zone + file rows + progress (F-TRF-001, identical UX).
3. Upload → `POST /api/v1/albums/{id}/contributions/draft` (endpoint 42) → per-file SAS → `…/finalize` (endpoint 43) → `AlbumItem` rows (BlobRef shared).
4. The items appear in the grid immediately (AC-036-1 — no approval queue in MVP, D-23).

## Alternative flows

- **Owner uploads**: same path, owner token implicit (AC-036-2) — the owner is always a contributor.
- **Re-issued token** (owner adds a *second* contributor, or revokes): old tokens invalidated — the old link shows "Your contributor link has been updated — ask for a new one." (AC-036-5, EC-036-4, documented).
- **Contributor name optional**: no name → the grid shows the item without attribution (FR-036-5).

## Acceptance criteria

```gherkin
Given I invited a contributor
When they upload 2 images
Then both items appear in the grid for me and viewers, attributed to their name if given

Given I re-issue a contributor token
When the old token is used
Then it is invalidated (the updated-link state shows)
```

## Edge cases

- Non-image upload (a doc): accepted, shown with a file icon — albums are "photos first", not "photos only" (EC-036-1, documented).
- Contributor + owner upload the same file name: both kept — a gallery, not a transfer; no de-dup (EC-036-2, documented).
- Album full (cap, US-034-03): contributor's upload rejected with the cap named (EC-036-3).

## UI notes

- Contributor upload screen: drop zone + file rows + overall progress — the landing page, verbatim.
- "Your name" field: one line, optional, persisted for the token's life (the contributor sees it pre-filled on their next upload).

## Technical notes

- `ContributorToken (Id, AlbumId, Name?, ExpiresAtUtc?, RevokedAtUtc?)` (TA-3.2 addition); `AlbumItem (Id, AlbumId, BlobRefId, OriginalName, SizeBytes, ContentType, ContributorName?, SortOrder, CreatedAtUtc)` (separate table — the `FileItem` unification is D-23, documented).
- `album.item_added { albumId, fileId, contributor? }` event; telemetry `album_uploaded`.
- Token revocation: re-issue sets `RevokedAtUtc` on old tokens (one transaction, FR-036-1 "old tokens invalidated").

## Links

- Feature: `ALB-003-contribute-album.md` (FR-036-1/2/5, AC-036-1/2/5, EC-036-1/2/4)
- Related: F-TRF-001 (the engine), US-034-03 (the cap this hits), US-036-02 (where items land)
