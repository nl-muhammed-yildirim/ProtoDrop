# PROGRESS.md — Session State (read at every session start)

**Last updated:** 2026-08-31
This file is the **rolling state of the build**. A fresh AI session has no memory of previous sessions — this file is that memory.

**Rules (binding for the AI):**
1. Read this file **after** `AGENT.md`, before doing anything else (see AGENT.md §1).
2. Update **Section 1 and Section 3 as the last step of every session** — before ending, commit (if git is active).
3. One line per completed task, with the date. Do not delete history.
4. If a session ends mid-task, set `In progress` to that task and write exactly what remains.
5. Never edit the `Backlog` section here — `Milestone-Backlog.md` is the source of truth for tasks; this file only *references* it.

---

## 1. Current state

| Field | Value |
|---|---|
| **Current task** | T-002 (first non-`done` task in `Milestone-Backlog.md`) |
| **Milestone** | M0 — Foundation |
| **In progress** | T-002 — local tooling: `docker-compose.local.yml` (AGENT.md §5.1), `wa.api` Local launchSettings (§5.2), `GET /health` pinging SQL, Serilog wiring. (T-001 scaffold done: `src/wa.web` created + aligned, esproj, port 5173, vitest, landing.) |
| **Environment** | local: `C:\Users\myild\source\repos\ProtoDrop` (docs say `workspace\ProtoDrop` — repo was moved; treat `source\repos\ProtoDrop` as current). All planning/operating docs live under `docs/`. |
| **Git** | initialized on branch `main`; baseline commit 2026-08-31 (this session). "Dubious ownership" warning (NT AUTHORITY/SYSTEM vs `myild`) safe → use `safe.directory` exception. |
| **Open escalations** | see `ESCALATIONS.md` |

## 2. Completed tasks (append-only)

| Task | Done | Notes |
|---|---|---|
| — | | *no code tasks completed yet* |
| T-001 (web part) | 2026-08-31 | `src/wa.web` created via VS (create-vite react-ts) + aligned to requirements: `wa.web.esproj` (Vitest, build-on-build, dist output), dev port 5173 (was VS-generated 54184), vitest + jsdom + @testing-library/react dev deps + smoke test, `tsconfig.app.json` strict + `@/*` alias, landing UI per UI-Reference (tokens.css, DropZone, wordmark top bar), Vite scaffold removed. Gate green: `npm run lint` / `test:run` (1/1) / `build` (60.5 kB gz). `package.json` has `test: vitest` + `test:run: vitest run` (watch vs one-shot). |

## 3. This session / last session

**Session 2026-08-31 (T-001 closed / T-002 in progress):**
- **T-001 → done** (2026-08-31): marked in `Milestone-Backlog.md`. Scaffold + `src/wa.web` aligned to requirements (web gate green from prior session); `global.json` pins .NET 10; all 5 src + 3 test projects exist.
- **Docs now live under `docs/`**: `Milestone-Backlog.md`, `PROGRESS.md`, `Open-Decisions-and-Constants.md`, `ESCALATIONS.md`, etc. (repo was moved to `source\repos\ProtoDrop`; earlier docs referenced `workspace\ProtoDrop`).
- **Git**: repo initialized (was "not initialized") on branch `main`, zero commits in the prior state; this session adds the baseline commit. Box shows a "dubious ownership" warning (`NT AUTHORITY/SYSTEM` vs `myild`) — safe, handled with `safe.directory`.
- **Next**: T-002 (local tooling — `docker-compose.local.yml`, `wa.api` Local launchSettings §5.2, `GET /health` DB ping, Serilog wiring).

**Session 2026-08-31 (T-001 web stub aligned to requirements):**
- `src/wa.web` (created in VS today via create-vite react-ts) reworked to project requirements:
  - `wa.web.esproj`: `JavaScriptTestFramework = Vitest`, `ShouldRunBuildScript = true`, `BuildOutputFolder = dist/` (VS Build now runs the `package.json` build, matching TA-12.1 step 4).
  - Port **5173** everywhere (AGENT.md §5.2 / Getting-Started §4): `vite.config.ts` server+preview, `.vscode/launch.json` (also fixed `edge` → `msedge`).
  - Test gate: added `vitest` + `jsdom` + `@testing-library/react` (dev deps, TA-8.1 test tooling), `test`/`test:run` scripts, vitest block in `vite.config.ts` (jsdom, `src/**/*.{test,spec}.{ts,tsx}`, globals), smoke test `src/App.test.tsx`.
  - `tsconfig.app.json`: `strict: true` (TA-8.1), `DOM.Iterable`, `vitest/globals` types, `@/*` → `./src/*` (vite alias `@` added to `vite.config.ts`).
  - UI replaced Vite scaffold with the F-TRF-001 landing per UI-Reference.md: `src/styles/tokens.css` (full token set light/dark, focus ring, button styles), `src/features/landing/DropZone.tsx` (§4.1: role=button, aria-label, drag-over state, Choose files), top bar with "ProtoDrop" wordmark + Sign in; removed `assets/`, `App.css`, `index.css`.
  - Docs: `wa.web/README.md` rewritten (commands, structure, esproj notes), `CHANGELOG.md` entry.
- Gate verified green: `npm run lint` ✓ · `npm run test:run` ✓ 1/1 · `npm run build` ✓ (JS 60.51 kB gz, TA-8.4 budget ok).
- Open: no escalations raised (vitest/jsdom/@testing-library/react = TA-8.1 tooling, not new runtime packages; see decision log).

**Session 2026-08-31 (docs-only):**
- Created missing files: `QA-SCRIPTS.md` (manual verification scripts), `START-HERE.md` (bootstrap chain + first prompt), `.gitignore` (repo root — ready for `git init`).
- README: deduped the repeated `AGENT.md` index row; added `QA-SCRIPTS.md` + `START-HERE.md` to the document index.
- `03-technical-architecture.md`: TA-3.4 flag-key list synced to TA-13.2 (canonical seed list incl. `feature.search`/`dataExport`/`emailSettings`/`branding`/`ssoScim`); TA-17 header now says the table is canonical and `docs/adr/` files land when an ADR is amended.
- Verified all doc paths match the folder on disk, `C:\Users\myild\workspace\ProtoDrop` (the old `C:\dev\beamdrop` reference was already replaced in the previous session — re-confirmed here; START-HERE and PROGRESS now carry the same path).

**Session 2026-08-30 (docs-only):**
- Renamed project from `wetransfer-clone` / `BeamDrop` → **ProtoDrop** (folder + all file contents).
- Added **M5 backlog** (T-041…T-055, Phase 2 Collect/Sign/Albums) to `Milestone-Backlog.md` — closes the M5 gap.
- Fixed doc drift: `02-feature-plan.md` Phase 2 "Full spec", TA-0.2(4) migration path, TA-13.2 flag-key list, `Getting-Started-VS2026.md` paths, `04-production-plan.md` §10 scope note.
- Created: `PROGRESS.md`, `ESCALATIONS.md`, `LOCAL-LLM-RUNBOOK.md`, `AGENTS.md`.
- **Next action for the first code session:** T-001 — scaffold per `Getting-Started-VS2026.md` §3 (git init first).

## 4. Known open items (not escalations)

- Phase 2 open decisions D-21/D-22/D-23 (collect/sign/albums plan gating, grace days, conversion/thumbnail choices) — consolidated proposal in `ESCALATIONS.md` **E-001**; answer there, record in `Open-Decisions-and-Constants.md` Part 3.
- `docs/adr/` folder does not exist yet — TA-17 table is canonical until ADR files are written.
- Git not initialized yet: `.gitignore` exists; run the one-time steps in `START-HERE.md` §3 before T-001.
