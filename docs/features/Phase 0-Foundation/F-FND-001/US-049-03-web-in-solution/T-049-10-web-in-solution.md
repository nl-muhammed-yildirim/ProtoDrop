# T-049-10 — Wrap wa.web into the solution via its package.json

**Story:** US-049-03 | **Spec:** FR-049-5, TA-2.5, AC-049-3 (solution half) | **Size:** S
**Depends on:** T-049-09

---

## Context to read (only these)

- `US-049-03-web-in-solution.md` → happy path step 1 + Technical notes
- `../../F-FND-001-solution-scaffold.md` → FR-049-5 + Technical notes (esproj line)

## Instructions

1. Add `src/wa.web` to `src/wa.slnx` as a VS 2026 web project via its `package.json` (VS 2026: right-click solution → Add → Existing Web Project, or create the `wa.web.esproj` wrapper).
2. Keep the build systems separate — no script in either world should invoke the other's build.

## Exit check

- [x] Solution lists `src/wa.web` as a web project
- [x] `dotnet build` at repo root still 0 warnings / 0 errors (esproj doesn't affect the .NET build)
- [x] npm commands in `src/wa.web` unchanged and still green

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (solution src/wa.slnx has 8 projects; src/wa.web is a Vite+React project).
Task T-049-10 — bring the web project into the solution.
Read first (only): docs/features/Phase 0-Foundation/F-FND-001/US-049-03-web-in-solution/US-049-03-web-in-solution.md (happy path step 1 + Technical notes).
Do exactly:
1. Add src/wa.web to src/wa.slnx as a VS 2026 web project referenced via its package.json (VS 2026 Add → Existing Web Project, or create the wa.web.esproj wrapper per TA-2.5).
2. Verify the .NET build is untouched: dotnet build at repo root must stay 0 warnings / 0 errors.
Done when: the solution lists src/wa.web as a web project, dotnet build is still green, and npm ci/lint/test:run/build in src/wa.web are unchanged.
Constraints: do not couple the build systems — .NET and Node stay separate commands (TA-2.5).
```
