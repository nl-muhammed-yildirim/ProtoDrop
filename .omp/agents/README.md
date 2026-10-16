# .omp/agents — ProtoDrop subagent roles

Role files for spawning focused subagents via `task()`. Three-role pipeline; one task at a time in T-xxx order from `docs/Milestone-Backlog.md` (first non-`done` row).

## Pipeline

`architect` → (per task) `developer` → (per commit) `reviewer`

| Role | File | Job |
|---|---|---|
| Architect | architect.md | Design + shape features into AC-/EC- and bounded T-xxx tasks, grounded in TA-* / feature specs. Plans; does not implement (unless asked). |
| Developer | developer.md | Implement one fully-specified T-xxx: code bug-free against its ACs/ECs, run the AGENT.md §4 test gate, commit `feat: T-xxx {one line}`, mark the backlog row + PROGRESS.md. |
| Reviewer | reviewer.md | Judge a diff/commit for bugs and AC coverage; report structured findings (blocker / major / minor). Verifies; does not edit (unless asked). |

**Handoff contract:** architect emits T-xxx rows ready to paste into `docs/Milestone-Backlog.md`; developer implements exactly one such row; reviewer verifies that commit against the row's ACs/ECs and flags any gap back. Scope is bounded by the backlog row — no task crosses it silently (see each file's escalation section).

## Notes
- Independent T-xxx tasks (no shared symbols / type changes) can run in parallel via multiple `task()` calls; dependent ones stay ordered (interface before implementation, migration before use).
- Supersedes the former `.omp/agents/task-orchestrator.md` (retired 2026-10-15): its decomposition and dependency-mapping concerns are covered by architect plus running independent tasks in parallel.

## Invocation
Call `task()` with `agent: "architect" | "developer" | "reviewer"` and the task context — each child starts blank, so pass the T-xxx ID, its backlog row (scope + exit check), the ACs/ECs from its feature spec, and the relevant TA-* sections explicitly.
