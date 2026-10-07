# T-052-01 — Add the 15 POCO entities to wa.domain (BCL-only)

**Story:** US-052-03 | **Spec:** FR-052-1, FR-052-3, AC-052-3 | **Size:** M (15 files, no behavior)
**Depends on:** T-049-02 (wa.domain project exists with TA-2.2 references)

---

## Context to read (only these)

- `../../F-FND-004-domain-model-schema.md` → FR-052-1/3 + Technical notes
- `US-052-03-pure-domain.md` → happy path + Gherkin blocks
- TA-3.2 (architecture doc) → DDL column lists only

## Instructions

1. Add POCO entities to `wa.domain` for every TA-3.2 table — 15 total: Plan, AppUser, AuthToken, Transfer, BlobRef, FileItem, EmailRecipient, EmailSuppression, DownloadEvent, Subscription, StripeEvent, FeatureFlag, AuditLog, JobRun, IdempotencyKey.
2. Each POCO maps 1:1 to its DDL columns with the TA-3.1 conventions: timestamps `DATETIME2` UTC suffixed `...AtUtc`, IDs are client-generated `UNIQUEIDENTIFIER` (Guid in C#), soft-delete only on AppUser and Transfer (`DeletedAtUtc`).
3. **Zero external references** — no EF, Azure, MediatR; BCL only.

## Exit check

- [ ] All 15 POCOs compile in wa.domain
- [ ] `wa.domain.csproj` has zero NuGet/project references (AC-052-3)
- [ ] No EF attributes, DbContext, or configuration classes inside wa.domain

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10, src/wa.slnx; wa.domain exists).
Task T-052-01 — add the domain POCOs.
Read first (only): docs/features/Phase 0-Foundation/F-FND-004/F-FND-004-domain-model-schema.md (FR-052-1/3 + Technical notes), US-052-03-pure-domain.md (happy path + Gherkin blocks), and TA-3.2 DDL column lists only.
Do exactly:
1. Add POCO entities to wa.domain for every TA-3.2 table — 15 total: Plan, AppUser, AuthToken, Transfer, BlobRef, FileItem, EmailRecipient, EmailSuppression, DownloadEvent, Subscription, StripeEvent, FeatureFlag, AuditLog, JobRun, IdempotencyKey.
2. Each POCO maps 1:1 to its DDL columns using TA-3.1 conventions: timestamps DATETIME2 UTC suffixed ...AtUtc (DateTime in C#), IDs client-generated UNIQUEIDENTIFIER (Guid), soft-delete only on AppUser and Transfer via DeletedAtUtc.
3. Keep wa.domain BCL-only — no EF, Azure, or MediatR references; no attributes, no behavior.
Done when: all 15 POCOs compile and wa.domain.csproj still has zero NuGet/project references (AC-052-3).
Constraints: POCOs only — no methods beyond what the DDL implies; LimitsRecord arrives later with T-005 (F-FND-005), do not add it here.
```
