# T-049-04 — Create the solution and make the build gate green

**Story:** US-049-01 | **Spec:** FR-049-1, FR-049-3, AC-049-1/4 | **Size:** M (gate task)
**Depends on:** T-049-03

---

## Context to read (only these)

- `../../F-FND-001-solution-scaffold.md` → FR-049-1, AC-049-1/4 + Technical notes
- `US-049-01-open-and-build.md` → first Gherkin block

## Instructions

1. Create the solution at `src/wa.slnx` (VS 2026 slnx format — via VS 2026 or CLI; the final file must be `wa.slnx`).
2. Add exactly these eight projects: `wa.domain`, `wa.application`, `wa.infrastructure`, `wa.api`, `wa.workers`, `wa.domain.unit`, `wa.application.unit`, `wa.api.integration`. (The web project joins in T-049-10.)
3. Run `dotnet build` from the repo root and fix every warning/error until it is **0 warnings, 0 errors**.

## Exit check

- [x] `src/wa.slnx` exists and lists exactly the 8 projects above
- [x] `dotnet build` at repo root: 0 warnings, 0 errors
- [x] `dotnet --version` still reports 10.x (global.json active)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10 pinned via global.json).
Task T-049-04 — create the solution and get a zero-warning build.
Read first (only): docs/features/Phase 0-Foundation/F-FND-001/F-FND-001-solution-scaffold.md (FR-049-1, AC-049-1/4).
Do exactly:
1. Create the solution file at src/wa.slnx (VS 2026 slnx format; use VS 2026 or dotnet new sln + conversion — final name must be wa.slnx in src/).
2. Add exactly these projects: src/wa.domain, src/wa.application, src/wa.infrastructure, src/wa.api, src/wa.workers, tests/wa.domain.unit, tests/wa.application.unit, tests/wa.api.integration. No others.
3. Run `dotnet build` from the repo root; fix all warnings and errors until the build is 0/0 (typical fixes: remove unused template code in wa.api/Program.cs).
Done when: dotnet build at repo root reports 0 warnings / 0 errors and src/wa.slnx lists exactly those 8 projects.
Constraints: do not add new packages or change the reference graph; web project is NOT added yet (T-049-10 does that).
```
