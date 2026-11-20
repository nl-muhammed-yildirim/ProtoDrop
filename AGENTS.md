# Rules

> **Runtime scope:** the rules below describe the **Hermes + local Ollama** pipeline. When running under **Claude Code**, follow `CLAUDE.md` + `.claude/agents/*.md` + the `protodrop-cycle` skill instead (same roles and cycle; models Haiku/Opus/Sonnet; the VRAM, sync-spawn and no-polling rules do not apply). Role separation, one task at a time, and the `DECISION:` escalation format apply to both.

## Agent pipeline (Orchestrator → Architect → Coder → Reviewer)

The four roles are defined in `.agents/agents/*.md`, each pinned to its local Ollama model (`ollama/Qwen3.8:*`). The machine config that pins them is `C:\Users\myild\AppData\Local\hermes\config.yaml` → `delegation:` (backend `model_map`: architect/coder/reviewer options, `max_concurrent_children: 1`, `gateway_mode: inline`; NOTE a `sync_top_level: true` sits there too but is a decoy — only the protodrop-cycle plugin reads it).

| Role | Mode | Model | Job |
|---|---|---|---|
| Orchestrator | primary (default) | Qwen3.8:Orchestrator | Coordinates only — picks the CURRENT T-xxx task from `docs/Milestone-Backlog.md` and runs the pipeline below; does not plan, code, or review itself. |
| Architect | subagent | Qwen3.8:Architect | Runs CodeGraph to explore the codebase, then plans one T-xxx into a concrete implementation plan (design + TA-* citations + ordered steps) with a **Files for Coder** handoff listing every file to create/modify and what to change in each, no code edits. |
| Coder | subagent | Qwen3.8:Coder | Implements exactly that plan, runs the test gate (`docs/AGENT.md` §4), commits `feat: T-xxx {one line}`. |
| Reviewer | subagent | Qwen3.8:Reviewer | Verifies the commit against ACs/ECs + conventions; PASS/FAIL with blocker/major/minor findings, no edits. |

Cycle per task: **architect (plan) → coder (develop) → reviewer (review)** — review FAIL or major+ finding routes back to coder; only a PASS lets the orchestrator mark the row `done` and update PROGRESS/backlog.

## Role separation (director vs writer)
- **Only the Coder writes code and tests**: every file under `src/`, `tests/` (source + unit/integration/e2e tests) is written by a Coder subagent — never by the Orchestrator.
- The Orchestrator is director + checker: it may edit docs (`docs/`, plans, PROGRESS, backlog), settings/config (Hermes config.yaml, Ollama Modelfiles, docker-compose, launchSettings.json), and plan markdown; it verifies diffs against the plan, runs gates, and makes the `docs:` commits.
- When a code/test edit must land outside a normal Coder run (reviewer minor fix, dead-child leftover), dispatch a short **Coder fix round** with the exact edits listed in its context — not an Orchestrator edit, unless the user explicitly authorizes one.

## Subagents
- **Only ONE model works at a time** (local Ollama, 16 GB VRAM): while a subagent runs, its role model must stay resident in VRAM — any parent-side request loads the Orchestrator model and evicts it mid-stream.
- **Synchronous spawns only**: launch each subagent with `async: false` (foreground) and no timeout; wait for that same tool call to return before doing anything else. `delegation.gateway_mode: inline` in config.yaml makes top-level `delegate_task` calls JOIN synchronously — the spawn blocks until the child finishes, then its result arrives in that same call. (`sync_top_level: true` also sits in the delegation block but is a decoy — only the protodrop-cycle plugin reads it; don't chase it as the sync knob.)
- **No status checking while a child runs**: do NOT call `action='list'`, do NOT tail the live transcript to wait, and do NOT end your turn early expecting to poll later. The blocking spawn IS the wait — polling is what evicts the child's model (the historical bug). One blocking spawn per step (plan→develop→review), then read that same tool result for the verdict.
- No parallel subagent calls: spawn only ONE at a time (`max_concurrent_children: 1`); wait for its completion before spawning the next.
- Child sessions start blank — every spawn must carry full task context: T-xxx ID, scope + exit check from the backlog row, ACs/ECs list, relevant TA-* sections, and any plan or findings text.
- One T-xxx task at a time in backlog order (first non-`done` row); no task crosses its backlog row silently — escalations surface to the user (`DECISION:` format per `docs/AGENT.md` §8).

## Ready-column workflow (status transitions — user rule 2026-11-20)

The task queue is the **@ProtoDrop Projects v2 board's `Ready` column**, not just the backlog file. Tooling: `scripts/board-ready.py` (lists Ready items; empty stdout = nothing ready) and `scripts/board-status.py` (`get <n>` / `set <n> "<status>"` / `list`). Exact status names are case-sensitive: **Backlog | Ready | In progress | In review | Done** — mutations use the option IDs in the script, never a name.

Per task found in `Ready`:
1. Read its plan from `docs/plans/` (the path is derived from the issue body by `board-ready.py`; verify it exists before starting).
2. **Before** dispatching Coder: `python scripts/board-status.py set <n> "In progress"` — verify the printed status matches.
3. Run the Coder cycle for that task only (one at a time; full context in its spawn).
4. **Just before** setting the item to `In review`, the orchestrator (as director) commits the code (`feat: T-xxx {one line}`), pushes it, and creates a pull request from that commit — push via the headless recipe (user:token URL, helper disabled; no `gh` CLI PR creation needed) and open the PR with REST POST `/repos/<owner>/<repo>/pulls` using the gh token.
5. Then `python scripts/board-status.py set <n> "In review"`.

Do not start coding while the item is still `Ready`, do not leave it `In progress` once coding is done, and do not move it to `In review` before its commit + PR exist — the column IS the workflow state. If a run dies mid-task, re-set `In progress` before re-dispatching (it already was) or set back to `Backlog` only when escalating.

## Extension
- Before Codegraph extension execution, run auto compaction beforehand.
