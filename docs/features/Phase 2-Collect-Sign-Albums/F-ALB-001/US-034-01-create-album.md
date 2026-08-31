# US-034-01 — Create a shared album

**Feature:** F-ALB-001 — Create Album | **Status:** pending

---

**Story:** As a Pro user, I want to create an album for a set of photos that will keep growing, so that the people I share it with see one place that doesn't expire.
**Actor:** album owner (Pro/Business, plan-gated `Features.albums`).
**Goal:** a one-screen create flow — title (≤ 80), optional password, optional per-album max size — that stores an `Album` row and mints `{origin}/album/{linkId}` (FR-034-1/3).

## Preconditions

- User signed in, on Pro/Business (Free sees "Albums require Pro", AC-034-3).

## Happy path

1. My Files → **My Albums** → **New album** → card: title, password (optional, show/hide), max size (optional; default = owner's plan `STORAGE_QUOTA`, cap ≤ plan).
2. **Create** → `POST /api/v1/albums` (endpoint 39) → `Album` row + unique `LinkId` → `album.created` event.
3. The album appears in **My Albums** with its link, 0 items, status `Active`.
4. `GET /album/{linkId}` returns 200 with the title — and **no expiry is shown** (persistence is the point, FR-034-2, AC-034-1).

## Alternative flows

- **Free tier**: plan gate deep-links to the plan screen (AC-034-3, consistent with F-COL-001/F-SGN-001 gates).
- **Guest can't create**: sign-in required (the owner is an account user, FR-034-2).

## Acceptance criteria

```gherkin
Given I am on Pro
When I create an album "Client proofs"
Then an Album row exists and GET /album/{linkId} returns 200 with the title
And no expiry is shown

Given I am on the default (Free) plan
When I try to create an album
Then I see "Albums require Pro"
```

## Edge cases

- Same title twice: allowed — the `linkId` is unique, the title isn't (EC-034-4).
- Zero items: the grid shows the empty state "No photos yet." + (owner) an upload drop zone (EC-034-1).
- Cover unset: the first upload becomes the cover (FR-034-1, documented).

## UI notes

- Create card: title, password (optional), max size (optional input with plan-quota hint), **Create** primary.
- My Albums rows: cover thumb (64×64, `--radius-sm`), title, item count, size, status chip.
- Tone: "Create an album." — calm, no exclamation points.

## Technical notes

- `Album` DDL (F-ALB-001 TA-3.2 addition): `Id, OwnerAppUserId, Title, PasswordHash?, MaxSizeBytes?, CoverBlobRefId?, Status (0 Active, 1 Closed), LinkId CHAR(8) UQ, CreatedAtUtc` — **no `ExpiresAtUtc`** (FR-034-2).
- `CreateAlbumCommand`, `ListMyAlbumsQuery` (MediatR, `Albums/` area).
- Event `album.created { albumId, linkId, ownerId }`; telemetry `album_created`.
- Blob layout: `albums/{albumId}/files/{fileId}`, `albums/{albumId}/cover.jpg` (TA-3.5 addition).

## Links

- Feature: `ALB-001-create-album.md` (FR-034-1/2/3/4/6, AC-034-1/3)
- Related: F-BIL-001 (plan gate), US-034-02 (password), US-034-03 (size cap), F-ALB-002 (sharing this album)
