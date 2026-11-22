---
name: protodrop-review
description: Review a ProtoDrop task Hermes finished (board item In review) — find its PR from feature/<ID> into develop, run the Opus reviewer, then either merge the PR (clean PASS) or send every finding back to Hermes as a fix round (Ready + review file) and re-review later. Use when the user says "review task <n>", "review PR <n>", "review <US-xxx-yy>", or "/protodrop-review <n>".
argument-hint: "<issue number | US-xxx-yy | PR #n>"
---

# ProtoDrop review — review Hermes' PR, merge or send back

Process rule (user, 2026-11-22): Claude Code is **Architect + Reviewer**, Hermes is the Coder. After Hermes moves an item to `In review`, Claude reviews its PR:
- **clean PASS (zero findings of any severity)** → Claude merges the PR into `develop` and sets the item `Done`;
- **anything found (blocker / major / minor)** → Hermes fixes it: Claude writes the findings to a review file, sends the item back to `Ready` as a fix round, and re-reviews when it returns to `In review`. Claude does not fix code itself.

You never write `src/` or `tests/`. Tooling (Bash, repo root `C:/Users/myild/source/repos/ProtoDrop`):
- `GH=/c/Users/myild/AppData/Local/hermes/tools/gh/bin/gh.exe` (has `repo` + `project` scope); repo `nl-muhammed-yildirim/ProtoDrop`.
- `python scripts/board-status.py get|set|list`, `python scripts/board-watch.py release <n>`.

## 1. Find the task and its PR
1. Resolve to the **issue number** (PR number given → read its body/branch to find the issue; story id → search titles like `protodrop-start` step 1).
2. `python scripts/board-status.py get <n>` must be `In review`. Otherwise stop and report (Hermes isn't done, or it's already merged).
3. Read the issue body: `**Plan:**` path, `**Branch:**` `feature/<ID>`, any earlier `**Review:**` line (→ this is round N+1).
4. PR: `"$GH" pr list -R nl-muhammed-yildirim/ProtoDrop --head feature/<ID> --state open --json number,title,baseRefName,headRefOid,url`. Exactly one, base `develop` — otherwise stop and report.
5. Local checkout: `git status --short` clean of tracked changes (else stop), then `git fetch origin` → `git switch feature/<ID>` → `git merge --ff-only origin/feature/<ID>`. HEAD must equal the PR's `headRefOid`.

## 2. Review — spawn `reviewer` (Opus 5.5, wait for it)
The child starts blank. Prompt carries: repo root, issue # + PR # + URLs, story/task id, the issue body verbatim, plan path ("the contract"), diff range `origin/develop...HEAD` (list the commits: `git log --oneline origin/develop..HEAD`), AC/EC ids, and a **numbered checklist**:
1. file boundary vs the plan's FILES FOR CODER;
2. `python scripts/verify_plan.py --done <plan>`;
3..n. one item per AC/EC: wiring + a test that encodes it;
then golden rules (`docs/AGENT.md` §3), role gating / CSS tokens / reuse as relevant;
last: re-run the per-task gates from `docs/AGENT.md` §4 that the diff touches (skip `tests/wa.api.integration` unless the diff touches api/infrastructure — then run it, Docker must be up).
On a re-review (round ≥ 2) add the previous review file and: "verify every earlier finding is fixed; flag anything new".
Ask for the usual PASS/FAIL + blocker/major/minor (`file:line`) + AC coverage table.

Also check the PR's CI yourself: `"$GH" pr checks <pr> -R nl-muhammed-yildirim/ProtoDrop`. A failing required check counts as a finding.

## 3a. Clean PASS → merge
Only when the verdict is PASS with **zero** findings and CI is green (or has no checks):
1. `"$GH" pr merge <pr> -R nl-muhammed-yildirim/ProtoDrop --merge` (merge commit, house style `Merge pull request #N from …`). Fallback if the API refuses: local `git switch develop && git merge --ff-only origin/develop && git merge --no-ff feature/<ID> -m "Merge pull request #<pr> from nl-muhammed-yildirim/feature/<ID>"` then push develop (step 3b recipe).
2. Verify: `"$GH" pr view <pr> --json state,mergeCommit` → `MERGED`; `git fetch origin` and `git merge-base --is-ancestor <headRefOid> origin/develop`.
3. `python scripts/board-status.py set <n> "Done"`; `"$GH" issue close <n> -R nl-muhammed-yildirim/ProtoDrop --comment "Merged in #<pr> after review PASS."`. If the issue has a same-title duplicate still open, list it in the report (don't close it unasked).
4. `git switch develop && git merge --ff-only origin/develop`.

## 3b. Anything found → fix round for Hermes
1. Write `docs/reviews/<ID>-review-r<N>.md`: verdict, every finding as a numbered item (severity, `file:line`, what is wrong, what done looks like), the AC coverage table, gate results. Hermes executes this file verbatim — make each item unambiguous.
2. On `feature/<ID>`: `git add docs/reviews/<ID>-review-r<N>.md` → `git commit -m "docs: <ID> review round <N> — <k> findings"` → push the branch:
   `git -c credential.helper= push "https://x-access-token:$("$GH" auth token)@github.com/nl-muhammed-yildirim/ProtoDrop.git" feature/<ID>` (verify with `git ls-remote origin feature/<ID>`).
3. PR comment: `"$GH" pr comment <pr> -R nl-muhammed-yildirim/ProtoDrop --body-file <scratch>` — summary + findings list + path of the review file.
4. Issue body: replace (or add, after the `**Branch:**` line) the line ``**Review:** `docs/reviews/<ID>-review-r<N>.md` (round <N>)`` via `--body-file`; never touch the rest.
5. Board, in **this order** (the watchdog wakes Hermes only on changed output):
   1. `python scripts/board-status.py set <n> "Ready"`
   2. `python scripts/board-watch.py release <n>` — the next tick re-renders the block with a `review :` line → one fix-round wake for Hermes.
   Releasing before setting Ready would make the block vanish while `In review` and fire a false "completed" wake.
6. `python scripts/board-ready.py` shows the item with its `review` line. Stop: re-review when the user says the item is back in `In review`.

## Stop and surface to the user
PR missing / several PRs / base not `develop`; HEAD ≠ PR head; merge conflict or refused merge; CI red for reasons outside the diff (prove with a run on `origin/develop`); a finding that is really a spec question (`DECISION:` format, `docs/AGENT.md` §8); round ≥ 3 with the same finding still open.

## Report
1. **Task** — `#<n>`, PR `#<pr>`, round N. 2. **Review** — verdict, findings by severity, AC coverage, gates, CI. 3. **Outcome** — merged (merge sha, board Done, issue closed) or sent back (review file, PR comment, board Ready + released).
