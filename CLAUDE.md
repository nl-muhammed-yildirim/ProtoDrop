# ProtoDrop — Claude Code

@docs/AGENT.md

## Agent pipeline (Claude port of the Hermes setup)

| Role | Where | Model | Job |
|---|---|---|---|
| Orchestrator | main session (`.claude/settings.json` default model) + `/protodrop-cycle` skill | Haiku 5.5 | Picks the CURRENT task, runs plan → develop → review, verifies, does docs close-out. Never writes `src/` or `tests/`. |
| Architect | `.claude/agents/architect.md` | Opus 5.5 | CodeGraph/grep grounding → `docs/plans/<T>-plan.md` with `### FILES FOR CODER`, verify_plan.py, `docs:` plan commit. |
| Coder | `.claude/agents/coder.md` | Sonnet 5.5 | Implements the committed plan, per-task gate, one `feat:` commit (fix rounds: `fix:`). |
| Reviewer | `.claude/agents/reviewer.md` | Opus 5.5 | Read-only PASS/FAIL with blocker/major/minor findings + AC coverage. |
| PO | `.claude/agents/po.md` | Sonnet 5.5 | Docs-only task-file generation for a feature. |

Run a cycle: `/protodrop-cycle next` (or a task id, or `feature-end`).

- Role separation: only the `coder` subagent writes `src/` and `tests/`. The orchestrator edits docs/config and makes `docs:` commits; any other code edit goes through a Coder fix round unless the user explicitly authorizes it.
- One task at a time, one subagent at a time (the pipeline is sequential). Every spawn carries full context — subagents start blank.
- Escalations use the `DECISION:` format (`docs/AGENT.md` §8) and stop the cycle.
- Root `AGENTS.md` describes the Hermes/Ollama runtime; its VRAM / no-polling / `delegate_task` rules apply to Hermes runs only, not to Claude Code.
- Repo traps and machine quirks: `.claude/skills/protodrop-cycle/references/pitfalls.md`.
