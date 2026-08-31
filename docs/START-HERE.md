# START-HERE.md — First prompt for a fresh AI session

**Last updated:** 2026-08-31
One page. If you are an AI starting the **first code session**, or a human pasting the first prompt: this is the exact thing to do.

---

## 1. The bootstrap chain (read in this order, every session)

1. `AGENT.md` — operating rules (stack, golden rules, test gate, autonomy, escalation).
2. `PROGRESS.md` — where the build is (current task, what's done, next action).
3. `Milestone-Backlog.md` — `CURRENT` = first non-`done` task. Do **only** that task.
4. The task's spec files: `features/<Phase>/<feature-id>/<feature-id>.md` + its user-story files, and the TA sections the task names.
5. `LOCAL-LLM-RUNBOOK.md` — context budget + the prompt template (for local models).
6. `QA-SCRIPTS.md` — when a task's exit check is "manual pass in browser."

## 2. The first prompt (paste verbatim into the agent)

```text
You are building ProtoDrop. Read, in order: AGENT.md, PROGRESS.md,
Milestone-Backlog.md, LOCAL-LLM-RUNBOOK.md.

CURRENT task = first non-done row in Milestone-Backlog.md.
Restate in one short answer: task ID, one-line scope, the spec files you
need, and the exit check. Do not write code yet.

Then, when I say "go": implement only that task, run the test gate
(AGENT.md §4), mark it done in Milestone-Backlog.md, and update
PROGRESS.md as the last step. One commit: `feat: T-xxx {one line}`.
Escalate per AGENT.md §8 when a value is missing — do not invent.
```

## 3. Before the very first session (human, one-time)

- [ ] `cd C:\Users\myild\workspace\ProtoDrop`
- [ ] `git init && git add . && git commit -m "docs: planning + operating docs (T-000)"`
- [ ] Machine tools from `Getting-Started-VS2026.md` §1 (VS 2026, .NET 10 SDK, Node 20, Docker Desktop)
- [ ] Answer `ESCALATIONS.md` open entries (E-001) if you want Phase 2 defaults frozen — not blocking for M0.

## 4. If anything conflicts

`Milestone-Backlog.md` wins on task order; `features/` wins on acceptance criteria; `Open-Decisions-and-Constants.md` Part 2 wins on values. Record the conflict in `PROGRESS.md` "Known open items" and keep working unless it blocks the current task — then escalate.
