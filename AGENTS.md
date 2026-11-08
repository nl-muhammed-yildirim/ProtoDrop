# Rules

## Agent pipeline (Orchestrator → Architect → Coder → Reviewer)

The four roles are defined in `.agents/agents/*.md`, each pinned to its local Ollama model (`ollama/Qwen3.8:*`). The machine config that pins them is `~/.hermes/config.yaml` → `delegation:` (backend `model_map`: architect/coder/reviewer options, `max_concurrent_children: 1`, `sync_top_level: true`).

| Role | Mode | Model | Job |
|---|---|---|---|
| Orchestrator | primary (default) | Qwen3.8:Orchestrator | Coordinates only — picks the CURRENT T-xxx task from `docs/Milestone-Backlog.md` and runs the pipeline below; does not plan, code, or review itself. |
| Architect | subagent | Qwen3.8:Architect | Runs CodeGraph to explore the codebase, then plans one T-xxx into a concrete implementation plan (design + TA-* citations + ordered steps) with a **Files for Coder** handoff listing every file to create/modify and what to change in each, no code edits. |
| Coder | subagent | Qwen3.8:Coder | Implements exactly that plan, runs the test gate (`docs/AGENT.md` §4), commits `feat: T-xxx {one line}`. |
| Reviewer | subagent | Qwen3.8:Reviewer | Verifies the commit against ACs/ECs + conventions; PASS/FAIL with blocker/major/minor findings, no edits. |

Cycle per task: **architect (plan) → coder (develop) → reviewer (review)** — review FAIL or major+ finding routes back to coder; only a PASS lets the orchestrator mark the row `done` and update PROGRESS/backlog.

## Subagents
- **Only ONE model works at a time** (local Ollama, 16 GB VRAM): while a subagent runs, its role model must stay resident in VRAM — any parent-side request loads the Orchestrator model and evicts it mid-stream.
- **Synchronous spawns only**: launch each subagent with `async: false` (foreground) and no timeout; wait for that same tool call to return before doing anything else. `delegation.sync_top_level: true` in config.yaml makes top-level `delegate_task` calls JOIN synchronously — the spawn blocks until the child finishes, then its result arrives in that same call.
- **No status checking while a child runs**: do NOT call `action='list'`, do NOT tail the live transcript to wait, and do NOT end your turn early expecting to poll later. The blocking spawn IS the wait — polling is what evicts the child's model (the historical bug). One blocking spawn per step (plan→develop→review), then read that same tool result for the verdict.
- No parallel subagent calls: spawn only ONE at a time (`max_concurrent_children: 1`); wait for its completion before spawning the next.
- Child sessions start blank — every spawn must carry full task context: T-xxx ID, scope + exit check from the backlog row, ACs/ECs list, relevant TA-* sections, and any plan or findings text.
- One T-xxx task at a time in backlog order (first non-`done` row); no task crosses its backlog row silently — escalations surface to the user (`DECISION:` format per `docs/AGENT.md` §8).

## Extension
- Before Codegraph extension execution, run auto compaction beforehand.
