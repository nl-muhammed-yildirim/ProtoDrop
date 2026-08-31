# AGENTS.md

For AI agents / agentic tools working in this repository (Copilot, Cline, Roo, or any local LLM session):

1. **`AGENT.md`** — the operating manual. Read it first, every session. It defines the stack, golden rules, test gate, autonomy boundary, and escalation format.
2. **`PROGRESS.md`** — the rolling state: current task, what's done, next action. Update it as the **last step of every session**.
3. **`Milestone-Backlog.md`** — one `CURRENT` task at a time (first non-`done` row). Do only that task; its scope + exit check are there.
4. **`ESCALATIONS.md`** — pending decision requests. If you open one, follow the format in `AGENT.md` §8.
5. **`LOCAL-LLM-RUNBOOK.md`** — how to fit this project into a local model's context window (always-loaded files, prompt template, stop conditions).
6. **`QA-SCRIPTS.md`** — when the task's exit check is "manual pass in browser", run the matching scenario there.
7. Specs: `features/<Phase>/<feature-id>/` (feature file + one file per user story). Cite stable IDs (`F-*`, `FR-*`, `AC-*`, `TA-*`) — never paraphrase.

Definition of Done = `AGENT.md` §4 test gate + the task's exit check + `PROGRESS.md` updated.
