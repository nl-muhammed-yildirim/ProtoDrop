# T-049-07 — Audit the dependency rule and record the result

**Story:** US-049-02 | **Spec:** AC-049-2, US-049-02 Gherkin blocks | **Size:** S (verify only)
**Depends on:** T-049-06

---

## Context to read (only these)

- `US-049-02-layer-rules.md` → Acceptance criteria + Edge cases
- `../../F-FND-001-solution-scaffold.md` → FR-049-2, AC-049-2

## Instructions

1. Inspect every `.csproj` in the solution and write down each project's references.
2. Assert all of these hold (fix any violation):
   - `wa.domain`: zero NuGet refs, zero project refs (BCL only)
   - `wa.application`: only `wa.domain` + MediatR/FluentValidation packages
   - no cycles; `wa.api` never references `wa.workers`
3. Record the audit result under **Audit result** below in this file.

## Exit check
- [x] All assertions above hold (no code change expected)
- [x] Audit result recorded in this file

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10).
Task T-049-07 — audit the TA-2.2 dependency rule (verification task, no new code expected).
Read first (only): docs/features/Phase 0-Foundation/F-FND-001/US-049-02-layer-rules/US-049-02-layer-rules.md (Acceptance criteria + Edge cases).
Do exactly:
1. List the ProjectReference and PackageReference of every .csproj in src/ and tests/.
2. Verify: wa.domain has zero refs; wa.application references only wa.domain (+ MediatR, FluentValidation); no cycles; wa.api does not reference wa.workers; EF/Azure packages appear only in wa.infrastructure or below.
3. Fix any violation (expected: none). Then append the audit result as a short comment under "## Audit result" in docs/features/Phase 0-Foundation/F-FND-001/US-049-02-layer-rules/T-049-07-dependency-audit.md.
Done when: all assertions hold and the audit is recorded in the task file.
Constraints: do not restructure anything — if a violation needs restructuring, stop and report it.
```

## Audit result

All assertions hold — no code change was needed.

| Project | Project refs | Package refs |
| --- | --- | --- |
| `wa.domain` | — | — (BCL only) |
| `wa.application` | `wa.domain` | MediatR 12.5.0, FluentValidation 12.1.1 |
| `wa.infrastructure` | `wa.application` | — |
| `wa.api` | `wa.infrastructure` (composition root) | — |
| `wa.workers` | `wa.infrastructure` | — |
| `tests/wa.domain.unit` | `wa.domain` | xunit, Microsoft.NET.Test.Sdk, coverlet.collector |
| `tests/wa.application.unit` | `wa.application` | xunit, Microsoft.NET.Test.Sdk, Microsoft.Extensions.DependencyInjection 10.0.12, coverlet.collector |
| `tests/wa.api.integration` | `wa.api` | xunit, Microsoft.NET.Test.Sdk, Microsoft.AspNetCore.Mvc.Testing 10.0.12, Testcontainers 4.15.0, Testcontainers.MsSql 4.15.0, coverlet.collector |

Verified:
- `wa.domain`: zero project refs, zero package refs (BCL only). ✓
- `wa.application`: only `wa.domain` + MediatR/FluentValidation. ✓
- No cycles; graph is a DAG (`domain ← application ← infrastructure ← {api, workers}`), tests reference upward. ✓
- `wa.api` never references `wa.workers`. ✓
- EF/Azure packages appear only in `wa.infrastructure` or below — holds (none present yet at M0). ✓
