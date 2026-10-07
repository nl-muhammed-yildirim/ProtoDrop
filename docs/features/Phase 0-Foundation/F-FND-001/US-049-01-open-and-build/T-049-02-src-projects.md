# T-049-02 — Create the five src projects with TA-2.2 references

**Story:** US-049-01 | **Spec:** FR-049-1, FR-049-2, AC-049-1/2 | **Size:** M (5 projects)
**Depends on:** T-049-01

---

## Context to read (only these)

- `../../F-FND-001-solution-scaffold.md` → FR-049-1, FR-049-2, FR-049-4
- `US-049-02-layer-rules.md` → happy path + first Gherkin block
- `docs/03-technical-architecture.md` (repo root docs) → TA-2.1 and TA-2.2 sections only

## Instructions

1. Create the five projects under `src/` with exactly these references (TA-2.2):
   - `wa.domain` — classlib, **no** project refs, **no** packages (BCL only)
   - `wa.application` — classlib → `wa.domain`; packages: MediatR 12.5.0 + FluentValidation (latest stable)
   - `wa.infrastructure` — classlib → `wa.application`
   - `wa.api` — ASP.NET Core web project (`dotnet new web`) → `wa.infrastructure`
   - `wa.workers` — classlib → `wa.infrastructure`
2. No other packages anywhere (golden rule 1: TA-2.6 is the only package list at M0).
3. Verify no reference cycles and that `wa.api` never references `wa.workers`.

## Exit check

- [ ] All 5 projects exist under `src/` with the exact reference graph above
- [ ] `dotnet build` on each project succeeds (solution-level gate comes in T-049-04)
- [ ] `wa.domain.csproj` has zero `<PackageReference>` and zero `<ProjectReference>`

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10, clean/onion architecture).
Task T-049-02 — create the five src projects.
Read first (only): docs/features/Phase 0-Foundation/F-FND-001/F-FND-001-solution-scaffold.md (FR-049-1/2/4) and docs/03-technical-architecture.md sections TA-2.1 + TA-2.2 only.
Do exactly:
1. dotnet new classlib -n wa.domain -o src/wa.domain        (no refs, no packages — BCL only)
2. dotnet new classlib -n wa.application -o src/wa.application; add ProjectReference to wa.domain; add PackageReference MediatR Version 12.5.0 and FluentValidation (latest stable).
3. dotnet new classlib -n wa.infrastructure -o src/wa.infrastructure; add ProjectReference to wa.application.
4. dotnet new web -n wa.api -o src/wa.api; add ProjectReference to wa.infrastructure.
5. dotnet new classlib -n wa.workers -o src/wa.workers; add ProjectReference to wa.infrastructure.
6. Delete the template Class1.cs / placeholder files you don't need (keep Program.cs in wa.api).
Done when: all 5 projects build individually and the reference graph is exactly: domain→nothing, application→domain, infrastructure→application, api→infrastructure, workers→infrastructure. No cycles; api never references workers.
Constraints: no NuGet packages beyond MediatR + FluentValidation in wa.application (golden rule 1). Do not create tests or a solution yet.
```
