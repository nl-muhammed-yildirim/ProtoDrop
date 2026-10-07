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

- [ ] All assertions above hold (no code change expected)
- [ ] Audit result recorded in this file

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

*(fill in after running this task)*
