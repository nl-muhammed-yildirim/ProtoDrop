# F-ALB-001 — Create Album

**Priority:** P1 (Phase 2c) | **Phase:** 2c — Albums
**Spec source:** `02-feature-plan.md` F-ALB-001 (outline → remapped below as FR-034-*) | **Architecture:** TA-3.2, TA-3.5, TA-4.2
**Milestone tasks:** T-052 (M5)

---

## Description

Albums are **shared galleries** — a photographer's client proofing flow, a family photo drop, a team moodboard. Unlike a transfer (one-shot, expires in 7 days), an album **persists**: it doesn't run on the 7-day clock, can be updated by its owner, and can have multiple contributors. The album is its own entity (`Album`) — persistent, with a cover, an optional password, and a max size.

**Actors:** owner (account user, plan-gated), contributor (F-ALB-003), viewer (guest or link), operator.
**Value:** the "gallery" product line — the visual sibling of Collect, and the one that drives the mobile photo audience.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-034-1 | Create an album: **title** (≤ 80), **cover** (optional image, first upload if unset), **optional password**, **max size** (default = owner's plan `STORAGE_QUOTA`; per-album cap optional, ≤ plan). |
| FR-034-2 | `Album` entity (TA-3.2 addition, migration): `Id, OwnerAppUserId, Title, PasswordHash?, MaxSizeBytes?, CoverBlobRefId?, Status (Active|Closed), LinkId (unique), CreatedAtUtc`. **No `ExpiresAtUtc`** — persistence is the point (F-ALB-002). |
| FR-034-3 | Link: `{origin}/album/{linkId}` (same 8-char generator). |
| FR-034-4 | Plan-gated: `PlanContext.Features.albums` (D-23 proposed: Pro + Business). |
| FR-034-5 | Album storage counts against the owner's `STORAGE_QUOTA` (same meter as transfers, F-TRF-007-4); per-album cap enforced at upload. |
| FR-034-6 | Albums appear in a **My Albums** section of My Files (alongside Collections/Transfers, F-COL-001 pattern): title, item count, size, status. |

## Acceptance criteria

```gherkin
AC-034-1: A Pro user creates an album "Client proofs"
  Then GET /album/{linkId} returns 200 with the title
  And no expiry is shown (persistence)

AC-034-2: A password-protected album is opened
  Then the single-password gate is shown (F-ALB-002 pattern, F-TRF-003-6 semantics)

AC-034-3: A Free user (default plan) tries to create an album
  Then they see "Albums require Pro"

AC-034-4: An album's uploads exceed its max size
  Then the upload is rejected with the cap named
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-034-1 | Album with zero items | Grid shows empty state "No photos yet." + (for owner) an upload drop zone |
| EC-034-2 | Owner deletes account | Album re-homed as guest-owned (F-COL-001 EC-025-2 pattern), stays open |
| EC-034-3 | Cover image deleted by owner | Falls back to the first item (documented) |
| EC-034-4 | Two albums, same title | Allowed (title is not unique; the linkId is) |

## UI notes (UI-Reference §5.4 extension)

- Create: card — title, password (optional, show/hide), max size (optional, slider or input), **Create**.
- My Albums rows: cover thumb (64×64, `--radius-sm`), title, item count, size, status chip.
- Tone: "Create an album." — calm.

## Technical notes

- `Album` DDL (TA-3.2 addition, migration + ADR note):
  ```sql
  CREATE TABLE dbo.Album (
      Id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Album PRIMARY KEY,
      OwnerAppUserId    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Album_Owner REFERENCES dbo.AppUser(Id),
      Title             NVARCHAR(80) NOT NULL,
      PasswordHash      VARCHAR(256) NULL,
      MaxSizeBytes      BIGINT NULL,
      CoverBlobRefId    UNIQUEIDENTIFIER NULL CONSTRAINT FK_Album_Cover REFERENCES dbo.BlobRef(Id),
      Status            TINYINT NOT NULL,  -- 0 Active, 1 Closed
      LinkId            CHAR(8) NOT NULL CONSTRAINT UQ_Album_LinkId UNIQUE,
      CreatedAtUtc      DATETIME2 NOT NULL
  );
  ```
- Endpoints (TA-4.2 extension, ADR note): `POST /api/v1/albums` (39), `GET /api/v1/albums` (40, My Albums list).
- MediatR: `CreateAlbumCommand`, `ListMyAlbumsQuery`.
- Blob layout (TA-3.5 addition): `albums/{albumId}/files/{fileId}`, `albums/{albumId}/cover.jpg`.
- Events (TA-5.3 extension): `album.created { albumId, linkId, ownerId }`.
- Telemetry (TA-10.2 extension): `album_created`.

## Test plan

- Unit: linkId generation; title validation; size-cap math (per-album vs plan).
- Integration: AC-034-1…034-4; storage meter increments on album upload; plan gate.
- E2E: create → link opens → password gate (if set).

## User stories

| ID | Story | File |
|---|---|---|
| US-034-01 | Create a shared album | `US-034-01-create-album.md` |
| US-034-02 | Protect my album with a password | `US-034-02-album-password.md` |
| US-034-03 | Cap my album's size | `US-034-03-album-size-cap.md` |
