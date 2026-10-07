# T-052-02 — WaDbContext + IEntityTypeConfiguration per entity

**Story:** US-052-01 | **Spec:** FR-052-2, FR-052-3, AC-052-1 (model half) | **Size:** M
**Depends on:** T-052-01 (POCOs exist in wa.domain)

---

## Context to read (only these)

- `../../F-FND-004-domain-model-schema.md` → FR-052-2/3 + Edge cases (EC-052-1 only)
- `US-052-01-migrate-fresh-db.md` → happy path
- TA-3.2/TA-3.3 (architecture doc) → constraint names, indexes, defaults

## Instructions

1. Create `WaDbContext` in `wa.infrastructure/Persistence` with one `IEntityTypeConfiguration<T>` per entity (15 files).
2. Encode the TA-3.2 shape: all PK constraint names, all 8 FK constraints (`FK_AppUser_Plan`, `FK_Sub_U`, `FK_T_Super`, `FK_Transfer_Owner`, `FK_ER_T`, `FK_FileItem_T`, `FK_FileItem_B` + self-reference), the 7 default clauses, both filtered indexes, `UQ_ES` / `UQ_Transfer_LinkId` unique constraints, and `IX_Transfer_Owner` (DESC) + `IX_Transfer_Expiry` INCLUDE per TA-3.2/TA-3.3.
3. Leave FK `ON DELETE` unspecified — EF omits it = SQL Server default NO ACTION, which matches the DDL (EC-052-1). Do **not** "fix" it with CASCADE.

## Exit check

- [ ] The model compiles and `WaDbContext` discovers all 15 configurations
- [ ] No configuration class or DbContext leaks into wa.domain (AC-052-3 still holds)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; 15 POCOs already in wa.domain).
Task T-052-02 — add the EF model layer.
Read first (only): docs/features/Phase 0-Foundation/F-FND-004/F-FND-004-domain-model-schema.md (FR-052-2/3 + EC-052-1), US-052-01-migrate-fresh-db.md (happy path), and TA-3.2/TA-3.3 constraint names, indexes, and defaults.
Do exactly:
1. Create WaDbContext in src/wa.infrastructure/Persistence with one IEntityTypeConfiguration<T> per entity (15 files).
2. Encode the TA-3.2 shape verbatim: all PK constraint names, all 8 FK constraints (FK_AppUser_Plan, FK_Sub_U, FK_T_Super, FK_Transfer_Owner, FK_ER_T, FK_FileItem_T, FK_FileItem_B + self-reference), the 7 default clauses, both filtered indexes, UQ_ES / UQ_Transfer_LinkId unique constraints, and IX_Transfer_Owner (DESC) + IX_Transfer_Expiry INCLUDE per TA-3.2/TA-3.3.
3. Leave FK ON DELETE unspecified — EF omits it = SQL Server default NO ACTION, which matches the DDL (EC-052-1); do not add CASCADE.
Done when: the model compiles, WaDbContext discovers all 15 configurations, and wa.domain stays BCL-only.
Constraints: EF Core 10.x is the only EF home — everything persistence-related lives in wa.infrastructure/Persistence; no seed data yet (T-052-04 owns it).
```
