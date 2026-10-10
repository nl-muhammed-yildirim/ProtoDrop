# F-FND-003 — CI Pipeline (PR Gate)

**Priority:** P0 (foundation) | **Phase:** 0 — Foundation (M0)
**Spec source:** `Milestone-Backlog.md` T-003, TA-12.1 | **Architecture:** TA-12.1, TA-14.1, TA-14.2
**Milestone tasks:** T-003

---

## Description

The PR gate is the quality contract of the whole project: no merge to `main` without lint, unit tests, integration tests (real SQL + Azurite via Testcontainers), web typecheck/test/build, and a Docker image build. At M0 the pipeline runs on GitHub Actions (`ci.yml`) with six jobs; pushing the dev image to ACR is gated behind the dev registry existing (E-003). The gate encodes AGENT.md §4 so that "Definition of Done" is machine-checked, not remembered.

**Actors:** developer (opens PRs), AI session (runs the same commands locally before claiming done), reviewer (reads the red/green verdict).
**Value:** every later task's exit check includes "full §4 gate green" — this feature makes that phrase mean something identical on every machine.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-051-1 | `.github/workflows/ci.yml` runs on every PR with the TA-12.1 job set: `lint-web` (eslint), `lint-dotnet` (`dotnet format --verify-no-changes`), `unit` (wa.domain.unit + wa.application.unit, 100 % pass gate), `integration` (wa.api.integration with Testcontainers SQL + Azurite), `web` (tsc typecheck + eslint + `test:run` + build), and `docker` (multi-stage build of `wa-api`). |
| FR-051-2 | The Docker job builds the image from `src/wa.api/Dockerfile` (multi-stage: publish → `aspnet:10.0`) tagged `wa-api:ci-<sha>`; at M0 it does **not** push (push gated on repo variable `AZURE_CONTAINER_REGISTRY`, E-003). |
| FR-051-3 | Line endings are locked by `.gitattributes` so `dotnet format --verify-no-changes` is deterministic across Windows local / Linux CI. |
| FR-051-4 | The pipeline fails the PR when any job fails; job output must show which test/step failed (no silent green). |
| FR-051-5 | `appsettings.json` holds env-neutral defaults only — no secrets in the repo (TA-13.1); CI injects what it needs via OIDC federation later (TA-12.2). |

## Acceptance criteria

```gherkin
AC-051-1: A sample PR is opened
  When ci.yml runs on the PR
  Then all six jobs complete and the gate verdict is green

AC-051-2: A failing unit test blocks the merge
  Given a PR that breaks one domain unit test
  When ci.yml runs
  Then the unit job fails and the PR shows red with the failing test name in output

AC-051-3: The Docker image builds
  Given a PR touching src/wa.api
  When the docker job runs
  Then an image tagged wa-api:ci-<sha> is built (multi-stage, aspnet:10.0 base) and no push happens without AZURE_CONTAINER_REGISTRY set

AC-051-4: Formatting is deterministic
  Given a PR that changes C# files with correct formatting
  When the lint-dotnet job runs
  Then dotnet format --verify-no-changes passes (no spurious line-ending failures on Linux CI)
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-051-1 | Prettier step before the dev-dep exists | Gated `if: false` in ci.yml until prettier is added (T-003 note) — eslint is the active web linter at M0 |
| EC-051-2 | Testcontainers needs Docker on CI runners | GitHub-hosted runners have Docker; self-hosted must document it (TA-14.3 shape) |
| EC-051-3 | ACR push attempted before dev registry exists | Push step commented + gated on repo variable (E-003); owner "you" per Preflight P-03 |
| EC-051-4 | Web build output size regression | Build job reports gzip size; TA-8.4 budgets are the reference, not a hard gate at M0 |

## UI notes

- None (CI only).

## Technical notes

- Jobs map 1:1 to AGENT.md §4 commands — keep them in sync when either changes.
- `dotnet format src/wa.slnx --verify-no-changes` is structural no-op until an `.editorconfig` lands (T-003 note).
- Dockerfile at `src/wa.api/Dockerfile`; `.dockerignore` keeps repo-root context ~3 MB.
- CD to staging/prod (`cd-staging.yml`, `cd-prod.yml`) lands with M4 launch prep — this feature is the PR gate only (TA-12.1 top half).

## Test plan

- Exit check: "PR gate runs green on a sample PR" (T-003) — verified locally by running every job's commands and via one real PR.
- Negative: break a unit test in a scratch branch → red verdict with the test name visible.

## User stories & implementation tasks

| ID | Story / Task | File |
|---|---|---|
| US-051-01 | Know my PR is safe to merge before I click | `US-051-01-pr-gate/US-051-01-pr-gate.md` |
| US-051-02 | Catch format drift between Windows and Linux | `US-051-02-format-determinism/US-051-02-format-determinism.md` |
| US-051-03 | Have an image ready before deploy exists | `US-051-03-image-build/US-051-03-image-build.md` |

**Implementation tasks:** one file per task — each story folder holds its story .md + its task files (context-friendly; execute top-to-bottom).

| Story | Task | File | Status |
|---|---|---|---|
| US-051-02 | T-051-01 Add .gitattributes locking line endings | `US-051-02-format-determinism/T-051-01-gitattributes.md` | ☑ |
| US-051-03 | T-051-02 Multi-stage Dockerfile + .dockerignore for wa-api | `US-051-03-image-build/T-051-02-dockerfile.md` | ☑ |
| US-051-01 | T-051-03 Create .github/workflows/ci.yml with the TA-12.1 job set | `US-051-01-pr-gate/T-051-03-ci-yml-jobs.md` | ☐ |
| US-051-01 | T-051-04 Sample PR runs the gate green (and red when it should) | `US-051-01-pr-gate/T-051-04-sample-pr-green.md` | ☐ |

**Story done when:** all tasks checked + full AGENT.md §4 gate green + the story's ACs verified. Then T-003 can be marked `done` in `Milestone-Backlog.md`.
