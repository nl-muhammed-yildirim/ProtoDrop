# Feature-end gate (once per feature, after its last T-xxx closes)

Triggered when every task row of the feature is `done`. AGENT.md §4 defines the suites. Order: run all four test gates fresh → feature-level review (below) → docs close-out commit → push + PR (credential rules in SKILL.md).

## Test gates — commands and order

1. `dotnet test tests/wa.domain.unit --nologo -v q` (fast)
2. `dotnet test tests/wa.application.unit --nologo -v q` (fast); then `dotnet test tests/wa.workers.unit --nologo -v q` (fast — f-zip/f-email; added 2026-11-17)
3. `dotnet test tests/wa.api.integration --nologo -v q` — **long** (Testcontainers per class; Docker daemon must be up). Run as background terminal with notify and continue independent work meanwhile; wait via process_manage(action=wait, timeout≈540) — it may still need a second wait.
4. Frontend: `cd src/wa.web && npm run lint` / `npm run test:run` / `npm run build` (independent of the dotnet suites — can overlap step 3). From repo root also `node scripts/check-telemetry-pii.mjs` and `node scripts/check-telemetry-pii.mjs selftest` (T-012-05, both must exit 0).

All four green → gate satisfied. Report per-suite counts in the close-out (e.g. "domain.unit 52/52, application.unit 338/338, api.integration N/N, wa.web 281/281"). The feature-end run is the FRESH-evidence moment: re-run everything even if Coder already ran it at HEAD.

## Pre-flight

- Check Docker daemon before starting api.integration (`docker version`): Testcontainers pulls per class and a dead daemon only surfaces minutes in. Daemon up but `docker ps` empty is normal — no pre-started containers needed; the suite spins its own.
- Confirm working tree clean at HEAD first: a dirty tree means an uncommitted close-out or Coder leftover, which muddies "green at which commit?".

## Frontend flake triage rule

wa.web has intermittent timing flakes (observed in RecipientPage.test.tsx and StagingList.test.tsx — the list grows; treat any single-file failure as suspect until proven). When a full-suite run shows failures but Coder reported all-green at HEAD:
1. Re-run the failing file alone 3× (`npx vitest run <file> --reporter=basic`).
2. If isolated runs pass and/or a second full run is green → flake, not regression: report "full suite green; X has an intermittent timing flake (passes in isolation)" — do NOT treat as blocker or re-dispatch Coder.
3. Only consistent isolated failure = real failure → route to the fixer role.

WHY: vitest timer-dependent tests are load-sensitive on this machine (local-model box, parallel suite run starves timers); flakes have appeared both before and after commits — never attribute a flake to the latest commit without isolated-run evidence.

## Feature-level review (once per feature, before docs close-out)

After every T-xxx row is done AND the full gate is green: dispatch a Reviewer over the WHOLE diff (`git diff <merge-base>..HEAD`), not a re-review of individual commits. The spawn must carry the per-commit map (each task's commit + scope) and each task's known minors — otherwise the reviewer re-grades what already passed.

Checklist to grade:
1. **FR coverage**: every FR in the spec → which commit(s) satisfy it; flag partial or silently dropped.
2. **AC/EC table**: one row per AC/EC — proving commit + test pin, PASS/FAIL.
3. **Closed-list integrity**: `ProblemCodes.cs` count unchanged from base (`grep -c '= "'`) and the client errorMap exhaustive over all codes with no stale entries (FR-013-6 class of check).
4. **Cross-task consistency**: for every file touched by 2+ task commits — no duplicate/conflicting handlers or orphaned state; each task's surfaces coexist in the same component.
5. **No dangling minors**: every minor from per-task reviews is either fixed or explicitly carried in PROGRESS.md.

If the Reviewer dies mid-loop (known class — see SKILL.md reviewer variant), re-derive each checklist item yourself against live source and apply the verdict as director; the docs commit notes 'review PASS orchestrator-applied after reviewer repetition-loop death'. Two sub-cases: if it died AFTER completing structural checks while composing the report, its trace tail shows every check — reconstruct the verdict from trace + your own re-verification rather than re-running all of them. If it was mid-computation when it died (e.g. WCAG contrast math), treat that finding as UNRESOLVED: recompute it independently against live source before classifying it, and classify regression-vs-pre-existing by diffing the file at the base commit (`git show <base-sha>:<path>`) — identical values mean pre-existing frozen-contract scope; then check UI-Reference/spec for which milestone owns that scope (e.g. WCAG AA on P0 surfaces is a later feature's row, not this one). A minor finding routes to a short Coder fix round with the exact old/new strings listed in its context — comment-only fixes need only lint re-run.

## Docs close-out shape (single `docs:` commit, orchestrator-authored)

Task-level (per T-xxx):
- PROGRESS.md: patch Last-updated line (`T-011-01…T-011-N done, all review PASS round 1; feature-end gate pending`) AND append the task's entry inside that feature's paragraph. Locate the append point with `grep -n "<prev task> done" docs/PROGRESS.md | tail` + read the line end — do NOT eyeball a long file.
- Spec status table (`docs/features/Phase 0-MVP/<F>/F-TRF-xxx-admin.md`): flip the row ☐→☑. (If a task's docs commit didn't touch the spec — some features only update PROGRESS per task — flip all remaining rows together at feature-end instead.)

Feature-level (last T-xxx only):
- Milestone-Backlog.md: mark the feature-end/close-out row done — mirror the previous feature's close-out commit exactly (`git show <prior-closeout-sha> -- docs/Milestone-Backlog.md` shows the diff shape). **Exception: a milestone row spanning multiple features (e.g. T-027 covers F-TRF-015…017) stays `pending` until its LAST feature closes** — record "part N done" in PROGRESS §1 and leave the row, so the later features' scope isn't silently closed.
- Open-Decisions-and-Constants.md Part 3: append a decision-log entry when a reviewer finding is DEFERRED rather than fixed (scope ruling + which feature owns it), mirroring prior entries' column format.
- PROGRESS.md: append the "feature COMPLETE" sentence with final suite counts.

Commit message pattern: `docs: T-0xx-N done — reviewer PASS round 1; PROGRESS + spec status table (orchestrator)` — feature-level mirrors the prior close-out's wording. All changes in ONE commit (PROGRESS + backlog + spec table together), never split.

## Pitfalls

- api.integration is the long pole: start it FIRST (background) and run the frontend gate while it runs, so total wall time ≈ max(integration, frontend) instead of their sum.
- Don't claim "integration green" from Coder's earlier run — Testcontainers state + container images can change between runs; the feature-end gate exists to produce fresh evidence at the final commit.
- If a suite fails at the gate, the fix routes through the normal pipeline (plan → coder → reviewer) like any other task — no direct orchestrator edits unless it's a one-line doc/test fix already proven by Coder's transcript.
