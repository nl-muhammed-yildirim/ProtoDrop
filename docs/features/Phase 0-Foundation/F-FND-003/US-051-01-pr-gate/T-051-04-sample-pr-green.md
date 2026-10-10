# T-051-04 — Sample PR runs the gate green (and red when it should)

**Story:** US-051-01 | **Spec:** AC-051-1/2, FR-051-4 | **Size:** S (verification only — no new files)
**Depends on:** T-051-03 (ci.yml), T-051-02 (Dockerfile)

---

## Context to read (only these)

- `US-051-01-pr-gate.md` → both Gherkin blocks + Edge cases
- `../../F-FND-003-ci-pipeline.md` → AC-051-1/2 + Test plan

## Instructions

1. Open a sample PR (docs-only change is fine) and confirm all six jobs run and the verdict is green.
2. Negative check: on a scratch branch, break exactly one domain unit test → open a PR → the `unit` job fails and the output **names the failing test**.
3. Locally first: run every job's command per AGENT.md §4 before opening the PR — CI confirms, it doesn't surprise (US-051-01 Alternative flows).

## Exit check

- [x] Sample PR → all six jobs green (AC-051-1)
- [x] Scratch branch with one broken domain test → red verdict with the failing test name in output (AC-051-2)
- [x] No silent green anywhere: every failure names its step/test

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; ci.yml + Dockerfile + .gitattributes all in place).
Task T-051-04 — verify the PR gate end-to-end.
Read first (only): docs/features/Phase 0-Foundation/F-FND-003/US-051-01-pr-gate/US-051-01-pr-gate.md (both Gherkin blocks + Edge cases) and F-FND-003-ci-pipeline.md (AC-051-1/2 + Test plan).
Do exactly:
1. Run every job's command locally per AGENT.md §4 first — CI confirms, it doesn't surprise.
2. Open a sample PR (docs-only change is fine) and confirm all six jobs run green: lint-web, lint-dotnet, unit, integration, web, docker.
3. Negative check: on a scratch branch break exactly one domain unit test, open a PR, and confirm the unit job fails with the failing test name visible in output; then delete the scratch branch.
Done when: AC-051-1 (green sample PR) and AC-051-2 (red verdict names the failing test) are both verified on real runs — not assumed.
Constraints: "green on retry" is not a pass at M0 — investigate flaky web builds before re-running; Testcontainers pull failures mean fix the runner, not the code.
```
