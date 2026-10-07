# T-049-03 — Create the three test projects with smoke tests

**Story:** US-049-01 | **Spec:** FR-049-6, AC-049-1 | **Size:** M (3 projects)
**Depends on:** T-049-02

---

## Context to read (only these)

- `../../F-FND-001-solution-scaffold.md` → FR-049-6 + Test plan section
- `US-049-01-open-and-build.md` → happy path steps 3–4

## Instructions

1. Create xUnit test projects under `tests/`:
   - `wa.domain.unit` → references `wa.domain`
   - `wa.application.unit` → references `wa.application`
   - `wa.api.integration` → references `wa.api`; add packages `Microsoft.AspNetCore.Mvc.Testing` (latest stable for .NET 10) + `Testcontainers` and `Testcontainers.MsSql` (latest stable — used from T-002/T-004 on)
2. Add one trivially passing test to each project (e.g. an assertion on a constant). The integration smoke test must **not** start containers or the API host yet.
3. If the integration test can't see the `Program` type later, an empty `public partial class Program { }` file in `wa.api` is the fix — add it now to avoid churn.

## Exit check

- [ ] `dotnet test tests/wa.domain.unit` → 1 pass
- [ ] `dotnet test tests/wa.application.unit` → 1 pass
- [ ] `dotnet test tests/wa.api.integration` → 1 pass (no Docker required)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10, xUnit pinned).
Task T-049-03 — create the three test projects with one passing smoke test each.
Read first (only): docs/features/Phase 0-Foundation/F-FND-001/F-FND-001-solution-scaffold.md (FR-049-6 + Test plan).
Do exactly:
1. dotnet new xunit -n wa.domain.unit -o tests/wa.domain.unit; add ProjectReference to src/wa.domain.
2. dotnet new xunit -n wa.application.unit -o tests/wa.application.unit; add ProjectReference to src/wa.application.
3. dotnet new xunit -n wa.api.integration -o tests/wa.api.integration; add ProjectReference to src/wa.api; add PackageReference Microsoft.AspNetCore.Mvc.Testing, Testcontainers, Testcontainers.MsSql (latest stable).
4. Replace each template test with one trivially passing smoke test (no Docker, no API host startup).
5. Add an empty `public partial class Program { }` file to src/wa.api so WebApplicationFactory can resolve the entry point later.
Done when: dotnet test passes in all three projects (1 pass each) without Docker running.
Constraints: no other packages; smoke tests must stay trivial — real coverage arrives with T-002+.
```
