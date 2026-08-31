# LOCAL-LLM-RUNBOOK.md — How to Run This Project with a Local LLM

**Last updated:** 2026-08-30
`AGENT.md` says *what* the AI must do. This file says *how* to feed it a task that fits a local model's context window, and when to stop. Read it once per machine setup.

---

## 1. The model

| Item | Value |
|---|---|
| Model | Qwen3.8 27B (GGUF, Unsloth Dynamic IQ4_XS, ~14–15 GB) |
| Hardware | RTX 5070 Ti (16 GB VRAM) / Ryzen 9 9800X3D |
| Context | run at **64K** (16 GB VRAM ceiling; native 262K but don't spend it all) |
| Thinking mode | **on** for design/decision work, off for mechanical edits |
| Sampling (thinking) | temp 1.0, top_p 0.95, top_k 20, no min_p, no presence penalty |
| Known behavior | stable to ~54K real depth; expect a quality spill cliff between 27K and 54K → **keep prompts under 45K tokens** |

If you change model or context size, edit this table first — it drives the budget in §2.

## 2. Context budget (the core discipline)

A full prompt = system + always-loaded files + task files + the request. Budget: **≤ 45K tokens of files** (~180 KB of markdown) to stay clear of the spill cliff.

### 2.1 ALWAYS loaded (every prompt)

| File | Why | Approx. size |
|---|---|---|
| `AGENT.md` | operating rules, autonomy boundary, test gate | ~4 KB |
| `PROGRESS.md` | where the build is, what's done, next action | ~3 KB |
| `Open-Decisions-and-Constants.md` Part 1–2 | values the AI may/may not invent | ~6 KB |
| The **task's** row in `Milestone-Backlog.md` | scope + exit check (paste the row, not the file) | ~1 KB |

**Hard cap: ~14 KB.** Everything beyond this is either a spec file or cited by ID.

### 2.2 Task files (paste in full)

- The feature spec file: `features/<Phase>/<feature-id>/<feature-id>.md` (~6 KB).
- The user-story file(s) the task covers (`US-*.md`, 3–8 KB each — paste only the stories the task names; if a task covers all stories of a feature, paste all of them).
- The matching TA sections from `03-technical-architecture.md` **when the task says so** (e.g. a schema task → TA-3.2 DDL; an endpoint task → TA-4.2 + TA-4.1.3). Copy those sections by heading, not the whole file.

### 2.3 Cite, don't paste

For `02-feature-plan.md` and `03-technical-architecture.md`: when a spec references an ID (FR-025-3, TA-3.6, ADR-009), the AI should be able to act from the feature file + the ID. Paste the referenced section only when the feature file clearly depends on details it doesn't contain. This is the single biggest lever: `03` is 61 KB — pasting it whole eats half the context.

### 2.4 The prompt template (paste per task)

```text
{AGENT.md §0.3 template from 03-technical-architecture.md, with}
FEATURE_ID: {F-xxx-nnn}
TA_SECTIONS: {TA-3.2, TA-4.2#28-29, ...}
SCOPE: {the task's Scope cell from Milestone-Backlog.md}
AC_IDS: {AC-xxx-n list}
EC_IDS: {EC-xxx-n list}

Context (always loaded): AGENT.md, PROGRESS.md, Open-Decisions Part 1–2.
Task files: <paste feature file + story files + cited TA sections>

Working rules for a local model:
1. One task only. Do not start T-{n+1} until the exit check in Milestone-Backlog.md passes.
2. If you need a value not in Open-Decisions Part 2 and not specced, STOP and write an escalation entry in ESCALATIONS.md (format in AGENT.md §8). Do not invent.
3. When done: mark the task `done` (date) in Milestone-Backlog.md, append a line to PROGRESS.md §2, update PROGRESS.md §1 "Current task".
4. Git: one commit per task, message `feat: T-xxx {one line}`.
```

## 3. Stop / escalate conditions

Stop and ask (write the escalation, do not guess) when:
- A limit/constant is used in two different values across files.
- A migration needs a column the DDL (TA-3.2 / feature files) doesn't define.
- The task requires a package not in TA-2.6 (needs an ADR line — ask).
- The test gate (§4 of AGENT.md) fails twice on the same root cause.
- Context pressure: if you had to drop a task file to fit the window, say so in `PROGRESS.md` — a human should know what the model never saw.

## 4. Session shape (recommended)

1. **Plan session** (thinking on): re-read `PROGRESS.md`, confirm the `CURRENT` task, paste the prompt template → let the model restate scope + AC list in one short answer. If it restates wrong, fix the prompt, not the code.
2. **Build session(s)** (thinking on for design decisions, off for boilerplate): implement + tests. One task per session if it's UI+API; a schema-only task can share a session with its first API task.
3. **Verify session** (thinking off): run the full test gate, run the task's exit check, update `PROGRESS.md` + `Milestone-Backlog.md`.

## 5. Machine notes

- VS 2026 as primary IDE; `src/wa.web` runs from a terminal inside VS (in-solution via package.json).
- Docker must be running for integration tests (Testcontainers) — see `AGENT.md` §5.
- `dotnet test` + `npm run lint && npm run test && npm run build` are the gate; copy the exact commands from `AGENT.md` §4.
