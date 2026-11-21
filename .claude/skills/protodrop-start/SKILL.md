---
name: protodrop-start
description: Start a ProtoDrop board task — find the GitHub Project (@ProtoDrop board) item by issue number or story/task id, read the issue, send it to the Opus Architect for planning, verify the plan, then move the board item to Ready for the Coder (Hermes). Use when the user says "start task <n>", "start this task <n>", "plan task <n>", or "/protodrop-start <n>".
model: claude-haiku-5-5
argument-hint: "<issue number | US-xxx-yy | T-xxx-NN>"
---

# ProtoDrop start — plan a board task and hand it to Ready

Process rule (user, 2026-11-21): Claude Code is **Architect + Reviewer**; the Coder is Hermes, which picks items up from the board's `Ready` column. "Start task <n>" runs exactly these three phases, in order:

| Phase | Who | Model |
|---|---|---|
| 1. Find the task on the board + read the issue | you (this skill) | Haiku 5.5 |
| 2. Plan it for the Coder, save + commit the plan | `architect` subagent | Opus 5.5 |
| 3. Verify the plan, link it in the issue, set status `Ready` | you (this skill) | Haiku 5.5 |

You do NOT plan, write `src/`/`tests/`, or code. One task per invocation. Tooling (Bash, repo root `C:/Users/myild/source/repos/ProtoDrop`):

- `GH=/c/Users/myild/AppData/Local/hermes/tools/gh/bin/gh.exe` — this gh login has the `project` scope; repo `nl-muhammed-yildirim/ProtoDrop`.
- `python scripts/board-status.py get <n>` / `set <n> "<Status>"` / `list` — statuses (case-sensitive): `Backlog | Ready | In progress | In review | Done`.
- `python scripts/board-ready.py` — lists Ready items with the plan path it parses from the issue body.

## Phase 1 — find and read the task (Haiku)

1. Resolve the argument to an **issue number**:
   - number (`386`, `#386`) → use it;
   - story/task id (`US-024-03`, `T-024-03-01`) → `"$GH" issue list -R nl-muhammed-yildirim/ProtoDrop --state open --search "<id> in:title" --json number,title` and pick the board item. Several open issues with the same title (the board has duplicates, e.g. #386/#387) → list them and ask the user which one; don't guess.
2. `python scripts/board-status.py get <n>` — must be on the board. Status:
   - `Backlog` → continue;
   - `Ready` → already planned: report the plan path and stop (re-plan only if the user asked);
   - `In progress` / `In review` / `Done` → stop and tell the user (don't re-plan work Hermes owns).
3. Read the issue in full: `"$GH" issue view <n> -R nl-muhammed-yildirim/ProtoDrop --json number,title,body,milestone,labels,url`. Capture title, milestone (= phase), feature id (`**Feature:** F-...` line), story id, AC/EC/FR ids, and the `## Links` section.
4. Find the local spec files (Glob under `docs/features/<Phase>/<feature-id>/`, phase dirs contain spaces): the feature spec `.md`, the user-story file for this story, and any `T-xxx-NN-*.md` task files in the story folder.
5. Plan path = `docs/plans/<story-or-task-id>-plan.md` (house naming, e.g. `docs/plans/US-024-03-plan.md`). If it already exists, tell the Architect to update it rather than start over.
6. `git status --short` + current branch. Uncommitted changes in `docs/plans/` → surface to the user before spawning.

## Phase 2 — plan (spawn `architect`, Opus 5.5, wait for it)

One `Agent` call, `subagent_type: "architect"`. The child starts blank — the prompt carries everything:
- repo root; issue number + URL; story/task id; the **issue body verbatim**; milestone/phase; feature id;
- the local spec / story / task-file paths from step 4; AC/EC/FR ids; TA-* ids from the issue's Links;
- the plan path from step 5, and: "write the plan with a `### FILES FOR CODER` table, run `python scripts/verify_plan.py <plan>`, commit ONLY the plan as `docs: <ID> plan — <title>`, end with your JSON block";
- "the Coder is Hermes (local Qwen) — make FILES FOR CODER and the ordered steps explicit enough to execute verbatim";
- anything the user said in this request.

## Phase 3 — verify, link, set Ready (Haiku)

Don't trust the self-report — check:
1. Plan file exists on disk; `python scripts/verify_plan.py <plan>` is clean or CREATE-only drift (one DRIFT line per CREATE file is expected at plan time; anything else → send back to the architect once with the output).
2. `git log -1 --format=%s` starts with `docs:` and `git show --stat HEAD` lists only the plan file.
3. Spot-check 2 cited `file:line` references against live source.
4. Architect returned `escalations` / `DECISION:` blocks → **STOP**: leave the item in `Backlog`, surface the DECISION blocks verbatim (`docs/AGENT.md` §8). Don't set Ready on an open question.
5. Architect died / no plan twice → stop and tell the user (no orchestrator-written plans in this flow).

Then:
6. **Link the plan in the issue body** (Hermes' `board-ready.py` finds the plan by parsing the body): if the body has no `docs/plans/` path yet, append a line ``**Plan:** `docs/plans/<ID>-plan.md` (commit <short-sha>)`` — fetch the body to a scratch file, append, `"$GH" issue edit <n> -R nl-muhammed-yildirim/ProtoDrop --body-file <file>`. Never rewrite the rest of the body.
7. `python scripts/board-status.py set <n> "Ready"` — the script re-reads and exits non-zero on mismatch.
8. `python scripts/board-ready.py` — confirm the item is listed with `plan file : docs/plans/<ID>-plan.md` (not `NOT FOUND`).

No push, PR or merge in this flow — Hermes handles that after coding (AGENTS.md "Ready-column workflow").

## Report (end of run)
1. **Task** — `#<n>` + story/task id + one-line scope.
2. **Plan** — path, commit sha, verify_plan result, design summary in 2–3 lines, FILES FOR CODER count.
3. **Board** — status now `Ready` (verified) and the board-ready line; or why it stayed in `Backlog`.

## Pitfalls
- `.claude/skills/protodrop-cycle/references/pitfalls.md` — verify_plan quirks, CRLF docs, git on this machine.
- MSYS path mangling: pass Windows paths to `gh --body-file` as `C:/...`, not `/c/...`, if gh complains.
