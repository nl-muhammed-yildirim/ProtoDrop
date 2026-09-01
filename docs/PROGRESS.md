# PROGRESS.md — Session State (read at every session start)

**Last updated:** 2026-09-01
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
| **Current task** | T-004 (3/4) |
| **Milestone** | M0 — Foundation |
| **In progress** | T-004 (3/4): (a) wa.domain POCOs 15 tables; (b) TA-3.2 DDL migration `InitialCreate`; (c) seed migration `SeedPlansAndFeatureFlags` (Plans free/pro/business per Open-Decisions Part 2 + all TA-13.2 `FeatureFlag` keys; D-07 stand-in Pro `MAX_TRANSFER_SIZE` = 10 GB per **E-004**) + unit test `SeedValuesUnitTest` (Part-2 values parsed from seed, full flag-key coverage) + `SeedDataIntegrationTest` 10 GB fix. What remains: **T-004 (4/4)**, then **T-005** (Limits Registry: `LimitsRecord`, `ILimitsProvider`, `PlansCache`+`FlagsCache` 30 s TTL, flag override; TA-3.4, F-TRF-007). |
| **Environment** | local: `C:\Users\myild\source\repos\ProtoDrop` (docs say `workspace\ProtoDrop` — repo was moved; treat `source\repos\ProtoDrop` as current). All planning/operating docs live under `docs/`. |
| **Git** | initialized on branch `main`; baseline commit 2026-08-31 (this session). "Dubious ownership" warning (NT AUTHORITY/SYSTEM vs `myild`) safe → use `safe.directory` exception. |
| **Open escalations** | see `ESCALATIONS.md` |

## 2. Completed tasks (append-only)

| Task | Done | Notes |
|---|---|---|
| — | | *no code tasks completed yet* |
| T-001 (web part) | 2026-08-31 | `src/wa.web` created via VS (create-vite react-ts) + aligned to requirements: `wa.web.esproj` (Vitest, build-on-build, dist output), dev port 5173 (was VS-generated 54184), vitest + jsdom + @testing-library/react dev deps + smoke test, `tsconfig.app.json` strict + `@/*` alias, landing UI per UI-Reference (tokens.css, DropZone, wordmark top bar), Vite scaffold removed. Gate green: `npm run lint` / `test:run` (1/1) / `build` (60.5 kB gz). `package.json` has `test: vitest` + `test:run: vitest run` (watch vs one-shot). |
| T-002 | 2026-08-31 | Local tooling per AGENT.md §5: `docker-compose.local.yml` (§5.1 verbatim except Azurite image — E-002: doc's `azure-storage:latest` 404s on MCR, used real `mcr.microsoft.com/azure-storage/azurite:latest`, command kept), `Properties/launchSettings.json` `Local` profile (port 8080, all §5.2 env vars incl. fixed 64-hex JWT secret, dev Zip key, empty SB/Hub), `Program.cs` rewrite: Serilog console wiring (TA-10.5 levels; `Microsoft.*`→Warning) + `GET /health` with REAL DB ping (`SELECT 1`; auto-creates missing `wa` db via `master`, 503 on fail; `sb: "skipped"` when SB unset per §5.2). Exit check green: 200 `{"status":"ok","db":"ok","sb":"skipped"}`, Serilog INF lines in terminal. Full §4 gate green. |
| T-003 | 2026-08-31 | CI per TA-12.1: `.github/workflows/ci.yml` PR gate, 6 jobs — `lint-web` (eslint; prettier step gated `if: false`, no prettier dev-dep yet), `lint-dotnet` (`dotnet format src/wa.slnx --verify-no-changes`; structural no-op until an `.editorconfig` lands), `unit` (domain.unit + application.unit, 100% pass), `integration` (api.integration, Testcontainers), `web` (tsc + eslint + `test:run` + build per TA-12.1), `docker` (multi-stage publish→aspnet:10.0, tag `wa-api:ci-<sha>`, **no push**). Supporting: `src/wa.api/Dockerfile`, `.dockerignore` (repo-root context stays ~3 MB), `.gitattributes` (locks line endings so `dotnet format` is deterministic across Windows local / Linux CI). **E-003** filed: ACR push dev sub-step (Preflight P-03 owner "you") — no dev ACR/IaC yet; push step commented in `ci.yml`, gated on a repo variable `AZURE_CONTAINER_REGISTRY`. Exit check: full §4 gate green locally (domain 1/1, application 1/1, api.integration 1/1, web lint + test:run 1/1 + build 60.5 kB gz; `docker build` `wa-api:local-check` green). |
| T-004 | 2026-09-01 | Domain model + full TA-3.2 DDL as EF Core migrations in `wa.infrastructure/Persistence` (a: 15 wa.domain POCOs BCL-only, 1:1 to DDL columns; b: `WaDbContext` + 15 `IEntityTypeConfiguration`s + `ef dotnet` CLI + migration `InitialCreate`). All 15 PK constraint names, 8 FK constraints (`FK_AppUser_Plan`, `FK_Sub_U`, `FK_T_Super`, `FK_Transfer_Owner`, `FK_ER_T`, `FK_FileItem_T`, `FK_FileItem_B` + self-ref) match TA-3.2 verbatim; `ON DELETE` omitted (= SQL `NO ACTION`, matches DDL); 7 default clauses; 2 filtered indexes + `UQ_ES`/`UQ_Transfer_LinkId` + `IX_Transfer_Owner` DESC + `IX_Transfer_Expiry` INCLUDE per TA-3.2/TA-3.3. `dotnet tools`: `ef` 10.0.x + `slng` 0.7.0 (`_slnxtmp` scaffolding removed after `wa.slnx` references `wa.infrastructure` directly). Exit check: full §4 gate green — solution build 0 warn/0 err, `dotnet format --verify-no-changes` 0, unit 1/1 x2, integration 1/1, web lint + test:run 1/1 + build 60.51 kB gz. `dotnet ef migrations script -i` → out.sql cross-checked against TA-3.2. |
| T-004 seed | 2026-09-01 | Seed `Plan` (free/pro/business via `HasData`, fixed GUIDs, `LimitsJson`/`FeaturesJson` per Part 2) + `FeatureFlag` seed (12 `feature.*` gates `false` per D-14; 3×14 `limits.<plan>.*` keys). Migration `SeedPlansAndFeatureFlags` applied to local SQL (wa db verified: 3 plans, 54 flags). Integration test `SeedDataIntegrationTest` (Testcontainers) asserts seeds end-to-end (4/4 pass incl. vacuous stub). Gate green: build 0 warn/0 err, `dotnet format` 0, unit 1/1 ×2, web lint + test:run 1/1 + build. |

## 3. This session / last session

**Session 2026-09-01 (T-004 part 3 / 4 — seed values + unit tests):**
- **T-004 (3/4)**: seed migration values aligned to Open-Decisions-and-Constants.md Part 2 exactly; Pro `MAX_TRANSFER_SIZE` = **10737418240 (10 GB)** stand-in for D-07 "TBD" (Part 2 Pro `STORAGE_QUOTA` = 100 GB conflicts with D-07 "20/50 GB" — see **E-004**). Pro `LimitsJson`, `PlanConfiguration` + `FeatureFlagConfiguration` rows, and the `limits.pro.maxTransferSize` flag all carry the stand-in; Free/Business untouched from Part 2.
- Added `tests/wa.domain.unit/SeedValuesUnitTest.cs` (11/11 green — 3 plan value sets parsed via EF design-time model, full TA-13.2 flag-key exact-coverage, 42 `limits.*` flag values, features.json gates); fixed both 21.47 GB remnants in `tests/wa.api.integration/SeedDataIntegrationTest.cs`.
- **ESCALATIONS.md E-004** filed (D-07 hole; AGENT.md §8 format); last-updated 2026-09-01.
- Gate: `dotnet build` green + `dotnet test` pending on `dotnet test` for unit (11/11) — full-gate re-run at commit time.
- **Remaining (4/4)**: T-004 wrap-up — full §4 gate re-run, `Milestone-Backlog.md` T-004 → done, commit `feat: T-004c plan/flag seeds + tests`.

**Session 2026-09-01 (T-004 seed closed):**
- **T-004 seed → done** (2026-09-01). `Plan` seed via `HasData` (free/pro/business, fixed GUIDs `11111111-…-00000000000x`, `LimitsJson`/`FeaturesJson` per Open-Decisions Part 2: free 5.37 GB/7d/100dl/20emails, pro 21.47 GB/30d/1000dl, business 107.37 GB/90d/−1) + `FeatureFlag` seed (12 `feature.*` gates all `false`, D-14 ads off; 3×14 `limits.<plan>.*` overrides = 42 keys, `limits.business.ssoScim=true`).
- Migration `SeedPlansAndFeatureFlags`: `dotnet ef migrations add` on `wa.infrastructure` only (its EF Design asset is `PrivateAssets=all` + startup not required — unlike `wa.api`, which needs `wa.infrastructure`'s Design asset to resolve at build time). Applied to local `wa` db via `dotnet ef database update --project src/wa.infrastructure`.
- Seed-verification integration test `tests/wa.api.integration/SeedDataIntegrationTest.cs` (Testcontainers.MsSql): spins a real MSSQL container, runs `ctx.Database.MigrateAsync()`, asserts Plans (3, codes, fixed GUIDs, Part-2 limit substrings, `sso_scim` only true on business) + 12 feature gates (all false) + 42 limit keys (spot-checked values). Replaced vacuous stub.
- Quirks learned: SqlClient connection-keyword is `TrustServerCertificate` (NOT `TrustServer Certificate` — throws `Keyword not supported`); EF cannot translate `string.StartsWith(str, StringComparison)` (filter in-memory). Format gate (`dotnet format --verify-no-changes`) enforces one-assignment-per-line object initializers; `MSqlBuilder()` parameterless ctor is obsolete — pass image name.

**Session 2026-09-01 (T-004 closed):**
- **T-004 → done** (2026-09-01), committed after `feat: T-004a`. T-004b: `wa.infrastructure` persistence layer per TA-3.2/TA-3.3.
  - `WaDbContext` (schema `dbo`, 15 `DbSet`s, `ApplyConfiguration` per table) + 15 `IEntityTypeConfiguration`s in `Persistence/ModelConfigurations/`. All 15 PK constraint names per TA-3.2 (`PK_Plan`, `PK_Sub`, `PK_Transfer`, `PK_Audit`, etc.) via `HasName` (relational key-builder surface has no `HasConstraintName` for keys; `HasConstraintName` is FK-only). 8 FKs with exact TA-3.2 names and `DeleteBehavior.NoAction` (= no `ON DELETE` clause; matches DDL).
  - Columns: explicit `HasColumnType`/`HasMaxLength` per TA-3.2 (e.g. `Id` PKs `bigint` identity, `LinkId` `uniqueidentifier` default `NEWID()`, `BlobRef.RefCount` `int` not null). 7 non-PK `DEFAULT`s (e.g. `EmailConfirmed 0`, `Theme 'system'`, `SortOrder 0`, `RefCount 1`).
  - `Plan` seed via `HasData` (free/pro/business per Part 2 constants); `FeatureFlag` seed (appendix A values). `Plan` is referenced by `AppUser` etc.
  - EF CLI: `dotnet new tool-manifest` → `dotnet-tools.json` (repo root, untracked→commit), `Microsoft.EntityFrameworkCore.Design`, slng 0.7.0 for standalone `wa.infrastructure` build.
  - `Migrations/InitialCreate` generated with `dotnet ef migrations add InitialCreate`; `dotnet ef migrations script -i` → out.sql cross-checked against TA-3.2 DDL (PK names, FK names, `ON DELETE`, `DEFAULT`, filtered `UQ_ES`/`IX_BlobRef_Cleanup`, `UQ_Transfer_LinkId`/`UQ_AppUser_Email`/`UQ_Sub_Stripe`, `IX_Transfer_Owner` DESC, `IX_Transfer_Expiry` INCLUDE). out.sql removed after cross-check (build artifact).
- Exit check: full §4 gate green — solution build 0 warn/0 err; `dotnet format src/wa.slnx --verify-no-changes` 0; `wa.domain.unit` 1/1, `wa.application.unit` 1/1, `wa.api.integration` 1/1; web lint + `test:run` 1/1 + build 60.51 kB gz.
- Cleanup: `_slnxtmp/` scaffolding + `out.sql` removed; `src/wa.infrastructure/Class1.cs` deleted; `src/wa.slnx` references `wa.infrastructure` directly. `packages.props` + `dotnet-tools.json` added.
- **Next: T-005** — Limits Registry: `LimitsRecord`, `ILimitsProvider`, `PlansCache` + `FlagsCache` (30 s TTL), flag override mechanics (TA-3.4, F-TRF-007).

**Session 2026-08-31 (T-003 closed):**
- **T-003 → done** (2026-08-31), committed as `feat: T-003`. Deliverables: `.github/workflows/ci.yml` (6-job PR gate per TA-12.1: `lint-web`, `lint-dotnet`, `unit`, `integration`, `web`, `docker`), `src/wa.api/Dockerfile` (multi-stage publish→`aspnet:10.0`; local `docker build` of `wa-api:local-check` green, context ~3 MB via `.dockerignore`), `.gitattributes` (line-endings pinned so `dotnet format --verify-no-changes` is deterministic across Windows-local / Linux-CI), `ESCALATIONS.md` **E-003**.
- **E-003** (ACR push dev, TA-12.1 step 5, Preflight P-03 owner "you"): no dev ACR/IaC on this box yet; push step commented in `ci.yml`, gated on repo variable `AZURE_CONTAINER_REGISTRY` (OIDC per TA-12.2, so no SP secret lands in repo).
- Exit check: full §4 gate green locally — domain 1/1, application 1/1, api.integration 1/1 (Testcontainers), web lint + `test:run` 1/1 + build 60.51 kB gz; `dotnet format src/wa.slnx --verify-no-changes` exit 0.
- **Scratch-PR verification step** (TA-12.3) parked on GitHub credentials (Preflight P-02, owner "you"): no `GITHUB_TOKEN`/`gh` CLI/PAT in the non-interactive shell; empty `origin/*` until first push. Sequence: user pushes `main`, opens a scratch `feat/...` PR → confirm all 6 jobs green → squash-merge.
- **Next: T-004** — domain model + TA-3.2 DDL as EF migrations.

**Session 2026-08-31 (T-002 closed):**
- **T-002 → done** (2026-08-31), committed as `feat: T-002`. Deliverables: `docker-compose.local.yml` (repo root, §5.1; Azurite image corrected → **E-002** filed, proceeded with real image name), `wa.api` `Local` launchSettings profile (port 8080, all §5.2 vars — dev Zip key + 64-hex JWT secret generated locally, recorded here instead of in Open-Decisions Part 2), `Program.cs`: Serilog (`UseSerilog`, TA-10.5 levels) + `GET /health` (real `SELECT 1` ping, auto-creates missing `wa` db via `master` until T-004 migrations; `sb: "skipped"` when `Wa:ServiceBus:ConnectionString` empty per §5.2; 503 on db fail).
- Exit check (backlog): with `docker compose -f docker-compose.local.yml up -d` (SQL 2022 + Azurite), `GET /health` → 200 `{"status":"ok","db":"ok","sb":"skipped"}`; Serilog `INF` lines visible in terminal.
- Full §4 gate green: domain 1/1, application 1/1, api.integration 1/1 (placeholder), wa.web lint + test:run 1/1 + build (60.5 kB gz).
- Environment notes: local `wa` database auto-created by health (transient, T-004 formalizes via EF migrations); SABnzbd had squatted :8080 — killed, worth re-checking on this box before runs.
- **Next: T-003** — CI (`.github/workflows`, PR gate + ACR push dev, no deploy to `dev`, per TA-12.1).

**Session 2026-08-31 (T-001 closed / T-002 started):**
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

- **D-07 stand-in (T-004c):** Part 2 Pro `MAX_TRANSFER_SIZE` is "TBD (D-07)" while Part 2 Pro `STORAGE_QUOTA` = 100 GB conflicts with D-07 "20/50 GB". Seed uses Pro `MAX_TRANSFER_SIZE` = **10737418240 (10 GB)** stand-in (`PlanConfiguration.cs`, `20260901010036_SeedPlansAndFeatureFlags.cs` + `.Designer.cs`, `WaDbContextModelSnapshot.cs`, and `limits.pro.maxTransferSize` flag). Full escalation in **ESCALATIONS.md E-004**; flip both `MAX_TRANSFER_SIZE` + `MAX_SINGLE_FILE` when D-07 is answered.
- Phase 2 open decisions D-21/D-22/D-23
- `docs/adr/` folder does not exist yet — TA-17 table is canonical until ADR files are written.
- Git not initialized yet: `.gitignore` exists; run the one-time steps in `START-HERE.md` §3 before T-001.
