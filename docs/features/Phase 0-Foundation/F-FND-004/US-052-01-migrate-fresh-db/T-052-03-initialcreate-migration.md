# T-052-03 — InitialCreate migration, cross-checked against TA-3.2

**Story:** US-052-01 | **Spec:** FR-052-2/6, AC-052-1/4 | **Size:** M
**Depends on:** T-052-02 (WaDbContext + configurations)

---

## Context to read (only these)

- `US-052-01-migrate-fresh-db.md` → both Gherkin blocks + Edge cases
- `../../F-FND-004-domain-model-schema.md` → FR-052-6 + AC-052-1/4 + Technical notes (out.sql line)

## Instructions

1. Create the EF migration **`InitialCreate`** in `wa.infrastructure/Persistence`.
2. Apply it to a fresh local Docker SQL: `dotnet ef database update --project src/wa.infrastructure --startup-project src/wa.api`.
3. Script the migration (`dotnet ef migrations script -i`) → save as `out.sql`, then cross-check against TA-3.2 with **zero drift**: constraint names, defaults, index shapes (filtered cleanup index, DESC owner index, INCLUDE expiry index).

## Exit check

- [ ] Fresh SQL instance: after `dotnet ef database update` every TA-3.2 table exists with exact PK/FK constraint names
- [ ] Filtered indexes and unique constraints present: IX_BlobRef_Cleanup, UQ_ES, UQ_Transfer_LinkId, IX_Transfer_Owner (DESC), IX_Transfer_Expiry INCLUDE
- [ ] `out.sql` diff against TA-3.2 shows zero drift (T-004 exit evidence)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; WaDbContext + 15 configurations in place; local Docker SQL running).
Task T-052-03 — create and verify the InitialCreate migration.
Read first (only): docs/features/Phase 0-Foundation/F-FND-004/US-052-01-migrate-fresh-db/US-052-01-migrate-fresh-db.md (both Gherkin blocks + Edge cases) and F-FND-004-domain-model-schema.md (FR-052-6 + AC-052-1/4).
Do exactly:
1. Create the EF migration named InitialCreate in src/wa.infrastructure/Persistence.
2. Run dotnet ef database update --project src/wa.infrastructure --startup-project src/wa.api against a fresh local Docker SQL and confirm every TA-3.2 table exists with its exact PK and FK constraint names, plus IX_BlobRef_Cleanup (filtered), UQ_ES, UQ_Transfer_LinkId, IX_Transfer_Owner (DESC), and IX_Transfer_Expiry INCLUDE.
3. Script the migration with dotnet ef migrations script -i to out.sql and cross-check it against TA-3.2 — zero drift in constraint names, defaults, or index shapes is required.
Done when: AC-052-1 and AC-052-4 both hold on a real fresh instance, with out.sql kept as the T-004 exit evidence.
Constraints: InitialCreate is schema-only — no seed data (T-052-04 owns seeds); re-running on an already-migrated DB must be a no-op.
```
