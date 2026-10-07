# US-049-01 — Open the repo in VS 2026 and build everything

**Feature:** F-FND-001 — Solution & Project Scaffold | **Status:** done (T-001, 2026-08-31)

---

**Story:** As a developer starting work on ProtoDrop, I want to open the repo in Visual Studio 2026 and have every project build, so that I can start coding without first fixing the toolchain.
**Actor:** Developer (human or AI session), on any machine with VS 2026 + .NET 10 SDK + Node 20 LTS.
**Goal:** One `dotnet build` covers all five .NET projects; one npm command covers the web project.

## Preconditions

- Repo cloned at `C:\Users\myild\source\repos\ProtoDrop`.
- VS 2026 with ASP.NET and Web Development workload installed (Getting-Started-VS2026.md).

## Happy path

1. Developer opens `src/wa.slnx` in VS 2026.
2. All projects appear: wa.domain, wa.application, wa.infrastructure, wa.api, wa.workers, three test projects, and the web project.
3. `dotnet build` completes with 0 warnings / 0 errors.
4. Each test project runs its smoke test (`dotnet test`).

## Alternative flows

- **Web build:** from a terminal inside VS: `cd src/wa.web && npm ci && npm run lint && npm run test && npm run build`.
- **CI build:** GitHub Actions runs the same commands (T-003); local and CI results must agree.

## Acceptance criteria

```gherkin
Given a fresh clone of the repo on a machine with .NET 10 SDK
When I open src/wa.slnx in VS 2026 and run dotnet build
Then the build succeeds with 0 warnings and 0 errors
And all 5 src projects + the 3 test projects are listed in the solution

Given global.json pins .NET 10 with rollForward latestMajor
When I run dotnet --version at the repo root
Then a 10.x SDK version is reported
```

## Edge cases

- Missing SDK: VS prompts via `global.json`; install per Getting-Started-VS2026.md (EC-049-1).
- Node missing or wrong major: web project fails at `npm ci` — pin Node 20 LTS in the docs; no `.nvmrc` drift.

## UI notes

- None.

## Technical notes

- Solution file is `wa.slnx` (VS 2026); `src/wa.web` is included via its `package.json` (TA-2.5).
- The build gate in AGENT.md §4 runs exactly these commands — this story's ACs are the gate's definition at M0.

## Links

- Feature: `F-FND-001-solution-scaffold.md` (FR-049-1…6, AC-049-1/3)
- Architecture: TA-2.1, TA-2.5
- Milestone: T-001
