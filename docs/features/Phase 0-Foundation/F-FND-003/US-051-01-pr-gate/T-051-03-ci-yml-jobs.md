# T-051-03 — Create .github/workflows/ci.yml with the TA-12.1 job set

**Story:** US-051-01 | **Spec:** FR-051-1, FR-051-4, FR-051-5, AC-051-1 | **Size:** M (one workflow file)
**Depends on:** T-051-01 (.gitattributes — so lint-dotnet is deterministic), T-051-02 (Dockerfile + .dockerignore — so the docker job has something to build)

---

## Context to read (only these)

- `../../F-FND-003-ci-pipeline.md` → FR-051-1/4/5 + Technical notes
- `US-051-01-pr-gate.md` → happy path + first Gherkin block
- `docs/AGENT.md` (repo root docs) → §4 only

## Instructions

1. Create `.github/workflows/ci.yml` running on every PR with exactly six jobs, 1:1 to AGENT.md §4 commands:
   - `lint-web`: eslint in `src/wa.web`
   - `lint-dotnet`: `dotnet format src/wa.slnx --verify-no-changes`
   - `unit`: wa.domain.unit + wa.application.unit, 100% pass gate
   - `integration`: wa.api.integration with Testcontainers SQL + Azurite (GitHub-hosted runners have Docker — EC-051-2)
   - `web`: tsc typecheck + eslint + `test:run` (one-shot) + build; report gzip size (TA-8.4 budgets are reference, not a hard gate at M0 — EC-051-4)
   - `docker`: multi-stage build of `wa-api` from `src/wa.api/Dockerfile`, tagged `wa-api:ci-<sha>` (the Dockerfile itself lands in T-051-02 — until then the docker job is expected red; that is fine, the gate only needs to be green at story completion)
2. Any failed job fails the PR; job output must name the failing test/step (no silent green).
3. The prettier step stays gated `if: false` until its dev-dep lands (EC-051-1); eslint is the active web linter at M0.
4. No secrets in the repo — CI injects what it needs via OIDC federation later (TA-12.2).

## Exit check

- [ ] ci.yml defines exactly six jobs matching AGENT.md §4 commands 1:1
- [ ] The green verdict on a real sample PR is verified in T-051-04 (this task only creates the workflow)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10, src/wa.slnx; GitHub Actions available).
Task T-051-03 — create the PR gate workflow.
Read first (only): docs/features/Phase 0-Foundation/F-FND-003/F-FND-003-ci-pipeline.md (FR-051-1/4/5 + Technical notes), US-051-01-pr-gate.md (happy path + first Gherkin block), and docs/AGENT.md §4 only.
Do exactly:
1. Create .github/workflows/ci.yml that runs on every PR with exactly six jobs, 1:1 to AGENT.md §4 commands: lint-web (eslint in src/wa.web), lint-dotnet (dotnet format src/wa.slnx --verify-no-changes), unit (wa.domain.unit + wa.application.unit, 100% pass gate), integration (wa.api.integration with Testcontainers SQL + Azurite), web (tsc typecheck + eslint + test:run one-shot + build, report gzip size as reference only per EC-051-4), docker (multi-stage build of src/wa.api/Dockerfile tagged wa-api:ci-<sha>).
2. Any failed job fails the PR and its output names the failing test/step — no silent green.
3. Keep a prettier step gated with if: false until its dev-dep lands (EC-051-1); eslint is the active web linter at M0.
4. No secrets in the repo; CI injects via OIDC federation later (TA-12.2).
Done when: ci.yml defines exactly six jobs mirroring AGENT.md §4, and a sample PR runs them all (the docker job may still be red until T-051-04 — that is expected at this step).
Constraints: keep the job set in sync with AGENT.md §4 — adding a job requires updating both files; do not create the Dockerfile here (T-051-04 owns it).
```
