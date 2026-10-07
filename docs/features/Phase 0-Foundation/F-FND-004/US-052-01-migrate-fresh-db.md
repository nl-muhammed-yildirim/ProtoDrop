# US-052-01 — Migrate a fresh database and get the whole schema

**Feature:** F-FND-004 — Domain Model & Database Schema | **Status:** done (T-004, 2026-09-01)

---

**Story:** As a developer or CI job setting up a new environment, I want one migration command to create the complete schema, so that no feature ever has to "remember" which tables exist.
**Actor:** Developer (human or AI session), CD pipeline (pre-deploy step per TA-12.2).
**Goal:** `dotnet ef database update` on a fresh SQL Server yields exactly the TA-3.2 schema — nothing more, nothing less.

## Preconditions

- Local Docker SQL running (F-FND-002 / US-050-01).
- Migrations present in `wa.infrastructure/Persistence`.

## Happy path

1. Run `dotnet ef database update --project src/wa.infrastructure --startup-project src/wa.api`.
2. All tables from TA-3.2 are created with their exact constraint names.
3. Indexes (including the filtered cleanup index and DESC owner index) exist.

## Alternative flows

- **CI:** Testcontainers spins a fresh SQL 2022 per test run; the same migration applies (TA-14.3).
- **Deploy:** CD runs the migration before the new API image starts — migrations always precede cutover (TA-12.2).

## Acceptance criteria

```gherkin
Given a fresh SQL Server instance with no wa schema
When I run dotnet ef database update
Then every TA-3.2 table exists with its exact PK and FK constraint names
And the filtered index IX_BlobRef_Cleanup, UQ_ES, UQ_Transfer_LinkId, IX_Transfer_Owner (DESC), and IX_Transfer_Expiry INCLUDE are present

Given a migrated database
When I script the migrations to SQL and diff against TA-3.2
Then there is zero drift in constraint names, defaults, or index shapes
```

## Edge cases

- FK `ON DELETE` omitted = SQL default NO ACTION (matches DDL — EC-052-1).
- Re-running on a migrated DB: no-op (EF tracks applied migrations).

## UI notes

- None.

## Technical notes

- The migration is the T-004 deliverable; `out.sql` cross-check against TA-3.2 was part of its exit evidence.
- Later schema changes = new migration + ADR note (golden rule 4) — this story's ACs define what "matches" means for any future diff.

## Links

- Feature: `F-FND-004-domain-model-schema.md` (FR-052-2, FR-052-6, AC-052-1/4)
- Architecture: TA-3.2, TA-3.3, TA-3.7
- Milestone: T-004
