# Rules

## Agent pipeline (Orchestrator → Architect → Coder → Reviewer)

The four roles are defined in `.opencode/agents/*.md`, each pinned to its local Ollama model (`ollama/Qwen3.8:*`):

| Role | Mode | Model | Job |
|---|---|---|---|
| Orchestrator | primary (default) | Qwen3.8:Orchestrator | Coordinates only — picks the CURRENT T-xxx task from `docs/Milestone-Backlog.md` and runs the pipeline below; does not plan, code, or review itself. |
| Architect | subagent | Qwen3.8:Architect | Plans one T-xxx into a concrete implementation plan (design + TA-* citations + ordered steps), no code edits. |
| Coder | subagent | Qwen3.8:Coder | Implements exactly that plan, runs the test gate (`docs/AGENT.md` §4), commits `feat: T-xxx {one line}`. |
| Reviewer | subagent | Qwen3.8:Reviewer | Verifies the commit against ACs/ECs + conventions; PASS/FAIL with blocker/major/minor findings, no edits. |

Cycle per task: **architect (plan) → coder (develop) → reviewer (review)** — review FAIL or major+ finding routes back to coder; only a PASS lets the orchestrator mark the row `done` and update PROGRESS/backlog.

## Subagents
- No parallel subagent calls: spawn only ONE at a time; wait for its completion before spawning the next (local-model context).
- Child sessions start blank — every spawn must carry full task context: T-xxx ID, scope + exit check from the backlog row, ACs/ECs list, relevant TA-* sections, and any plan or findings text.
- One T-xxx task at a time in backlog order (first non-`done` row); no task crosses its backlog row silently — escalations surface to the user (`DECISION:` format per `docs/AGENT.md` §8).
