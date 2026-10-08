# T-049-01 — Pin .NET SDK via global.json

**Story:** US-049-01 | **Spec:** FR-049-3, AC-049-4, EC-049-1 | **Size:** S (one file)
**Depends on:** — (first task)

---

## Context to read (only these)

- `../../F-FND-001-solution-scaffold.md` → FR-049-3 + AC-049-4 block
- `US-049-01-open-and-build.md` → second Gherkin block only

## Instructions

1. Create `global.json` at repo root pinning .NET 10 with roll-forward (exact content below).
2. Run `dotnet --version` at repo root — it must report a 10.x SDK.

```json
{
  "sdk": {
    "version": "10.0.0",
    "rollForward": "latestMajor"
  }
}
```

## Exit check (must pass before marking done)

- [x] `global.json` exists at repo root with the content above
- [x] `dotnet --version` at repo root reports a 10.x SDK

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (docs-only baseline, .NET 10 / VS 2026).
Task T-049-01 — pin the .NET SDK.
Read first (only): docs/features/Phase 0-Foundation/F-FND-001/F-FND-001-solution-scaffold.md (FR-049-3, AC-049-4).
Do exactly:
1. Create global.json at the repo root with exactly this content:
   { "sdk": { "version": "10.0.0", "rollForward": "latestMajor" } }
2. Run `dotnet --version` from the repo root and confirm it reports a 10.x SDK.
Done when: global.json is committed AND dotnet --version reports 10.x at the repo root.
Constraints: do not create any projects or solution yet; no other files change.
```
