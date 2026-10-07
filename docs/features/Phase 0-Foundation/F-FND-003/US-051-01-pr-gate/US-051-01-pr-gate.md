# US-051-01 — Know my PR is safe to merge before I click

**Feature:** F-FND-003 — CI Pipeline (PR Gate) | **Status:** done (T-003, 2026-08-31)

---

**Story:** As a developer opening a pull request, I want the pipeline to run lint, unit tests, integration tests, and the web build automatically, so that I know my change is safe before a reviewer spends time on it.
**Actor:** Developer (human or AI session), author of any PR.
**Goal:** A green PR gate means "Definition of Done" per AGENT.md §4 — no manual checklist needed.

## Preconditions

- `.github/workflows/ci.yml` present (T-003).
- Docker available on the CI runner (for Testcontainers).

## Happy path

1. Developer opens a PR against `main`.
2. ci.yml runs all six jobs: lint-web, lint-dotnet, unit, integration, web, docker.
3. All jobs pass; the PR shows green.
4. Reviewer merges with confidence.

## Alternative flows

- **Red gate:** any job fails → PR is red; output names the failing test/step (AC-051-2).
- **Local pre-flight:** the same commands run locally via AGENT.md §4 before the PR is opened — CI confirms, it doesn't surprise.

## Acceptance criteria

```gherkin
Given a sample PR with no code changes beyond docs
When ci.yml runs on the PR
Then all six jobs complete green and the gate verdict is green

Given a PR that breaks exactly one domain unit test
When ci.yml runs
Then the unit job fails and the output names the failing test
And the PR cannot be merged while red (branch protection or convention)
```

## Edge cases

- Testcontainers pull failure: integration job shows the Docker error — fix the runner, not the code.
- Flaky web build: investigate before re-running; "green on retry" is not a pass at M0.

## UI notes

- None (CI only).

## Technical notes

- Job set mirrors TA-12.1 exactly; adding a job requires updating both files.
- The gate is the machine half of AGENT.md §4 — the human half is the task's own exit check in Milestone-Backlog.md.

## Links

- Feature: `F-FND-003-ci-pipeline.md` (FR-051-1, AC-051-1/2)
- Architecture: TA-12.1, TA-14.1, TA-14.2
- Milestone: T-003
