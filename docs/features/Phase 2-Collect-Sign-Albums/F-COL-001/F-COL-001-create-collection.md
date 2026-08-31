# F-COL-001 — Create Collection

**Priority:** P1 (Phase 2a) | **Phase:** 2a — Collect
**Spec source:** `02-feature-plan.md` F-COL-001 (outline → remapped below as FR-025-*) | **Architecture:** TA-3.2, TA-4.2, TA-3.4, TA-7.1
**Milestone tasks:** T-041, T-042 (M5)

---

## Description

Collect is the **inverted transfer**: the collector creates a URL that others upload **to** — for onboarding packets, client briefs, auditors, real-estate closings. A collection has a title, description, an optional due date, and an optional per-field setup (name / email / file required). It gets its own link (`{origin}/collect/{linkId}`, same 8-char Crockford scheme as transfers) and its **own entity** — `Collection`, not `Transfer`: do not reuse the transfer table (the two lifecycles are different, see F-COL-004).

**Actors:** collector (account user, plan-gated), contributor (guest), operator.
**Value:** the "many → one" flow is a whole product line (WeTransfer Collect); this feature is its foundation.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-025-1 | Create a collection: **title** (≤ 80 chars, required), **description** (≤ 500, optional), **due date** (optional, F-COL-004), **per-field setup**: name required / email required / files required (defaults: name required, email optional, files required). |
| FR-025-2 | URL: `{origin}/collect/{linkId}` — 8-char Crockford base32, no ambiguous chars (same generator + collision rule as F-TRF-002-1). |
| FR-025-3 | **Different entity from Transfer**: `Collection` table (TA-3.2 addition, migration) — `Id, OwnerAppUserId, Title, Description, DueAtUtc?, Status (Open|PastDue|Closed), LinkId (unique), CreatedAtUtc, ClosedAtUtc?`. No FK to `Transfer`; no reuse of `Transfer` fields. |
| FR-025-4 | Owner must be a signed-in account user; plan-gated via `PlanContext.Features.collect` (D-21 proposed default: Pro + Business; Free sees "Collect requires Pro"). |
| FR-025-5 | Collection caps (proposed constants, pending D-21 — read from config, never literals): `COLLECTION_MAX_ENTRIES = 200` (submissions), `COLLECTION_TITLE_MAX = 80`, `COLLECTION_DESC_MAX = 500`. |
| FR-025-6 | Owner can see the collection in a **My Collections** list (new section of My Files, F-TRF-009 screen extension) with entry count and due state. |

## Acceptance criteria

```gherkin
AC-025-1: A Pro user creates a collection "Client onboarding" with a due date
  Then it is stored as a Collection row
  And GET /collect/{linkId} returns 200 with the title and description

AC-025-2: A guest opens the collection link
  Then they see the collection page with the per-field setup rendered
  And no account is required

AC-025-3: Two collections are created
  Then their linkIds differ and each resolves to its own page

AC-025-4: A Free user (default plan) tries to create a collection
  Then they see "Collect requires Pro" (plan screen deep-link)
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-025-1 | linkId collision (astronomically unlikely) | Same rule as F-TRF-002: regenerate once, then 500 + telemetry |
| EC-025-2 | Owner deletes their account while a collection is open | Collection is **re-homed as guest-owned** (owner link removed, collection kept open for its life; entries kept) — same spirit as F-TRF-008-6, documented |
| EC-025-3 | Title with line breaks / emojis | Trimmed, line breaks stripped; emojis allowed |
| EC-025-4 | Due date in the past at creation | Accepted but immediately `PastDue` (F-COL-004) |

## UI notes (UI-Reference §5.4 extension)

- My Files: "Collections" section (above Transfers) — rows: title, entry count, due state chip (green/amber/gray), **New collection** primary.
- Create screen: single card — title, description, due date (optional, date picker), field setup (three toggles), **Create**.
- Tone: "Collect files from many people." — calm, no exclamation points.

## Technical notes

- `Collection` DDL (TA-3.2 addition, migration + ADR note):
  ```sql
  CREATE TABLE dbo.Collection (
      Id                UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Collection PRIMARY KEY,
      OwnerAppUserId    UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Collection_Owner REFERENCES dbo.AppUser(Id),
      Title             NVARCHAR(80)  NOT NULL,
      Description       NVARCHAR(500) NULL,
      DueAtUtc          DATETIME2 NULL,
      Status            TINYINT NOT NULL,  -- 0 Open, 1 PastDue, 2 Closed
      LinkId            CHAR(8) NOT NULL CONSTRAINT UQ_Collection_LinkId UNIQUE,
      CreatedAtUtc      DATETIME2 NOT NULL,
      ClosedAtUtc       DATETIME2 NULL
  );
  ```
- Endpoints (TA-4.2 catalog extension, ADR note): `POST /api/v1/collections` (25), `GET /api/v1/collections` (26, My Collections list).
- MediatR: `CreateCollectionCommand`, `ListMyCollectionsQuery` (TA-4.2a — new `Collections/` area).
- Events (TA-5.3 extension): `collection.created { collectionId, linkId, ownerId }`.
- Telemetry (TA-10.2 extension): `collection_created`.
- Plan gate: `FeaturesJson.collect` (F-BIL-001 shape extended; D-21).

## Test plan

- Unit: linkId generation + collision; title/desc validation; status enum.
- Integration: AC-025-1 (create → public GET 200), AC-025-3 (uniqueness), AC-025-4 (plan gate); guest GET works without cookie.
- E2E: create → link opens in new-tab (Playwright manual pass).

## User stories

| ID | Story | File |
|---|---|---|
| US-025-01 | Create a collection for incoming files | `US-025-01-create-collection.md` |
| US-025-02 | Get a link I can send to people | `US-025-02-collection-link.md` |
| US-025-03 | Decide what each sender must provide | `US-025-03-field-setup.md` |
| US-025-04 | Set a due date when creating | `US-025-04-create-with-due-date.md` |
