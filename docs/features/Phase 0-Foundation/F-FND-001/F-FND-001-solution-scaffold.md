# F-FND-001 — Solution & Project Scaffold

**Priority:** P0 (foundation) | **Phase:** 0 — Foundation (M0)
**Spec source:** `Milestone-Backlog.md` T-001 | **Architecture:** TA-2.1, TA-2.2, TA-2.3, TA-2.5, TA-2.6
**Milestone tasks:** T-001

---

## Description

The monorepo scaffold is the first deliverable of M0: every project that will ever exist in ProtoDrop exists here, wired to the right layer with no extra dependencies, so that later features (T-009 onward) can add code without re-deciding structure. The solution contains the five .NET projects (`wa.domain`, `wa.application`, `wa.infrastructure`, `wa.api`, `wa.workers`), the React SPA (`src/wa.web`) as an in-solution web project, and the test projects — all under one solution file that Visual Studio 2026 opens directly. The `.NET 10` SDK is pinned via `global.json` so every machine builds the same runtime.

**Actors:** developer (human or AI session), CI pipeline (builds the solution on every PR).
**Value:** structure is decided once, at the start; every later task's "where does this file go?" question has a pre-answered answer in TA-2.1.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-049-1 | The solution (`wa.sln`/`wa.slnx`) contains exactly the projects of TA-2.1: `src/wa.domain`, `src/wa.application`, `src/wa.infrastructure`, `src/wa.api`, `src/wa.workers`, `tests/wa.domain.unit`, `tests/wa.application.unit`, `tests/wa.api.integration`, and `src/wa.web` (Vite + React 18 + TypeScript). No other projects. |
| FR-049-2 | Project references follow the dependency rule TA-2.2 exactly: `wa.domain` → nothing; `wa.application` → `wa.domain`; `wa.infrastructure` → `wa.application`; `wa.api` and `wa.workers` → `wa.infrastructure`. No cycles; `wa.api` never references `wa.workers`. |
| FR-049-3 | `global.json` pins the .NET 10 SDK (`"rollForward": "latestMajor"`). The solution builds with zero warnings and zero errors under that SDK. |
| FR-049-4 | Package set per TA-2.6: `wa.domain` has **no** NuGet references (pure POCOs); `wa.application` adds only MediatR 12.5 + FluentValidation; nothing else is added without an ADR line (TA-17). |
| FR-049-5 | `src/wa.web` is a VS 2026 web project in the solution via its `package.json` (TA-2.5), pinned to React 18 + TypeScript strict + Vite 5, with dev server on port **5173** and build output to `dist/`. |
| FR-049-6 | Test projects exist per TA-2.1: `tests/wa.domain.unit` (xUnit), `tests/wa.application.unit` (xUnit), `tests/wa.api.integration` (WebApplicationFactory + Testcontainers). Each builds and runs its first smoke test. |

## Acceptance criteria

```gherkin
AC-049-1: A fresh machine checks out the repo
  When it runs dotnet build on the solution with .NET 10 SDK via global.json
  Then the build is green (0 warnings, 0 errors)
  And all 5 src projects + the test projects exist and are in the solution

AC-049-2: The dependency rule holds
  When a developer adds code to wa.domain
  Then it compiles with zero external references (BCL only)
  And no project reference violates TA-2.2 (verified by build + review)

AC-049-3: The web project is in the solution
  Given src/wa.web is a VS 2026 web project via package.json
  When npm ci && npm run dev runs from the project folder
  Then Vite serves on http://localhost:5173
  And npm run build emits dist/ with the landing page bundle

AC-049-4: The SDK pin works
  Given global.json pins .NET 10 (rollForward latestMajor)
  When dotnet --version runs in the repo root
  Then a 10.x SDK is reported and the build uses it
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-049-1 | Machine has only .NET 9 installed | `global.json` roll-forward allows latestMajor ≥ 10; if none, VS prompts for SDK install — documented in Getting-Started-VS2026.md |
| EC-049-2 | Developer adds a NuGet package "temporarily" | Golden rule TA-0.2(1): no package without an ADR line (TA-17); code review catches it |
| EC-049-3 | `wa.web` dev port 5173 already in use | Vite auto-increments; the canonical local port is 5173 (AGENTS.md §5.2 CORS value depends on it) — free the port or update CORS, don't rename |

## UI notes

- None (backend + tooling only). `src/wa.web` ships a minimal landing stub at this milestone; the full upload surface is F-TRF-001 / T-012.

## Technical notes

- Solution file: `src/wa.slnx` (VS 2026 solution format); `wa.infrastructure` referenced directly (no `_slnxtmp` scaffolding — removed after T-004).
- `wa.web.esproj` wraps the Vite project in the solution; Vitest is the web test runner (`test:run` = one-shot for CI, `test` = watch locally).
- `.gitattributes` locks line endings (added with T-003) so `dotnet format --verify-no-changes` is deterministic across Windows local / Linux CI.
- ADRs in play: ADR-015/016 (solution layout + web project shape) per T-001 spec links.

## Test plan

- Build gate: `dotnet build` green on VS 2026 (exit check, T-001).
- Web gate: `npm run lint && npm run test && npm run build` green in `src/wa.web`.
- Smoke: each test project runs at least one trivially passing test so the CI jobs have something to execute.

## User stories & implementation tasks

| ID | Story / Task | File |
|---|---|---|
| US-049-01 | Open the repo in VS 2026 and build everything | `US-049-01-open-and-build/US-049-01-open-and-build.md` |
| US-049-02 | Know exactly where new code goes | `US-049-02-layer-rules/US-049-02-layer-rules.md` |
| US-049-03 | Run the web app locally in the same solution | `US-049-03-web-in-solution/US-049-03-web-in-solution.md` |

**Implementation tasks:** one file per task — each story folder holds its story .md + its task files (context-friendly; execute top-to-bottom).

| Story | Task | File | Status |
|---|---|---|---|
| US-049-01 | T-049-01 Pin .NET SDK via global.json | `US-049-01-open-and-build/T-049-01-global-json-pin.md` | ☑ |
| US-049-01 | T-049-02 Create the five src projects with TA-2.2 references | `US-049-01-open-and-build/T-049-02-src-projects.md` | ☐ |
| US-049-01 | T-049-03 Create the three test projects with smoke tests | `US-049-01-open-and-build/T-049-03-test-projects.md` | ☐ |
| US-049-01 | T-049-04 Create the solution and make the build gate green | `US-049-01-open-and-build/T-049-04-solution-build-gate.md` | ☐ |
| US-049-02 | T-049-05 Add the canonical folder homes with one-line READMEs | `US-049-02-layer-rules/T-049-05-canonical-folders.md` | ☐ |
| US-049-02 | T-049-06 Prove the CQRS convention with a first trivial use case | `US-049-02-layer-rules/T-049-06-first-use-case.md` | ☐ |
| US-049-02 | T-049-07 Audit the dependency rule and record the result | `US-049-02-layer-rules/T-049-07-dependency-audit.md` | ☐ |
| US-049-03 | T-049-08 Scaffold src/wa.web (Vite 5 + React 18 + TS strict) | `US-049-03-web-in-solution/T-049-08-web-scaffold.md` | ☐ |
| US-049-03 | T-049-09 Web scripts, landing stub, and first Vitest test | `US-049-03-web-in-solution/T-049-09-web-scripts-stub.md` | ☐ |
| US-049-03 | T-049-10 Wrap wa.web into the solution via its package.json | `US-049-03-web-in-solution/T-049-10-web-in-solution.md` | ☐ |

**Story done when:** all tasks checked + full AGENT.md §4 gate green + the story's ACs verified. Then T-001 can be marked `done` in `Milestone-Backlog.md`.
