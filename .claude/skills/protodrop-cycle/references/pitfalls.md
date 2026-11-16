# ProtoDrop pitfalls — distilled from the Hermes `protodrop` skill (v1.4.9, ~40 cycles)

Harness-specific Hermes material (VRAM/single-model eviction, `delegate_task` placeholder validator, steer, model_map routing) is dropped — it does not apply to Claude Code subagents. Everything below is about the repo and this machine.

## Task lookup
- Milestone rows (T-0xx) live in `docs/Milestone-Backlog.md`. Feature subtasks (T-xxx-NN) are NOT backlog rows: they are the ☐/☑ table inside the feature spec and have a task file in the story folder. Current subtask = first ☐ row.
- Phase dirs contain spaces: `docs/features/Phase 0-MVP/F-TRF-016/`. Guessed paths like `0-MVP` fail.
- Trust the on-disk spec over names in a prompt (story id ≠ title has happened).
- A task's Depends-on can cite sibling tasks that come LATER — don't assume done; reconcile in the plan or DECISION.

## scripts/verify_plan.py
- Exit 0 clean / 1 drift / 2 unparseable. Checks FILES FOR CODER (CREATE must not exist, MODIFY must), cited src/tests paths + line refs, closed sets (ProblemCodes, TelemetryEvents wire names, CSS :root tokens), storage keys. docs/ citations are advisory only.
- Heading must be exactly `### FILES FOR CODER`, else exit 2.
- At plan time each CREATE file yields one DRIFT line — expected. Confirm every drift line is a CREATE path; commit in that state. `--done` after implementation must be clean.
- Backticked dotted lowercase identifiers anywhere in the plan (`window.open`, `a.b.c`) are checked as telemetry names → write them without backticks. `wa.*` namespaces and file extensions pass.
- verify_plan does NOT check SB topic/subscription pairs or endpoint paths — grep those against the real publisher and `[ServiceBusTrigger]` attributes yourself.
- Plan inline snippets can be shape-valid but language-invalid (C# keywords as anon-type members; redundant StreamReader when .NET 10 `Stream.ReadToEndAsync(ct)` exists). A coder correcting that is a legitimate deviation, not drift.

## Git hygiene
- Stage exact paths only (`git add "<path>"`), never `-A`/`.` — the repo root carries stray untracked files (e.g. `NUL`).
- After every child run sweep new untracked files at the repo root and in `docs/plans/` (MSYS-mangled names like `C:-Users-myild...`, probe plans) and delete them.
- Commits: plan `docs: T-xxx plan — …`, code `feat: T-xxx …`, post-review fix `fix: T-xxx …` (own commit, never folded into feat), close-out `docs: T-xxx done — …`.
- Mid-feature WIP from another agent: `git stash list` + `git status --short`; diff stashes against recent commits before applying/dropping.
- Branch base: `git merge-base feature/F-TRF HEAD` must equal `git rev-parse feature/F-TRF`; surface mismatches, don't rebase silently.

## Gates
- Per task: `dotnet test tests/wa.domain.unit` + `tests/wa.application.unit`; wa.web touched → `npm run lint && npm run test:run && npm run build` (`npm run test` = watch mode, hangs). Theme/contrast scripts when CSS tokens change.
- User rule 2026-10-27: full `tests/wa.api.integration` (Testcontainers, ~100+ tests, 15+ min) runs ONCE at feature end. Per task, only `--filter "FullyQualifiedName~<Class>"` (~1 min).
- Per-task gates only COMPILE the integration project, so broken old facts surface at feature end:
  - **Auth gate added to an existing route** → old guest tests get 401 UNAUTHENTICATED. Fix recipe (precedent `FinalizeAccountLimitsTests.cs`): seed admin user before host build, `Environment.SetEnvironmentVariable("Wa__AdminEmails", email)` before host build (role derives per request from config, EC-011-3); forge TA-9.1 cookie `Base64(userId|exp|sig)` with HMAC-SHA256 over the test secret, exp = now+300s.
  - **CS0535/CS0539** = a hand-rolled stub didn't get a new port member → copy shapes from a sibling worker-test stub. Compile error ≠ logic failure; rule it out first.
- Prove pre-existing: `git stash -u` → run only that test with `--filter` → `git stash pop`. Same failure on base = record in PROGRESS §1 with base SHA. A test outside your diff is not automatically pre-existing.
- Docker: check `docker version` before any Testcontainers run; start from `C:\Program Files\Docker\Docker\Docker Desktop.exe`, poll `docker ps`. After a killed run remove orphan containers.
- Long suites: use `run_in_background` (Claude Code notifies on exit) and do other gates + director diff review in parallel.

## wa.web
- Feature stylesheets are imported in `src/main.tsx` — a new stylesheet must be added there or by its component.
- `npm run build` = `tsc -b && vite build`; vitest does not type-check — type errors appear only at build.
- No ad-hoc vitest flags (`--quiet` crashes CAC). Use `npm run test:run` or `npx vitest run <path>`.
- jsdom here has no `window.localStorage` (only sessionStorage) — guard access (pattern `src/wa.web/src/core/auth/session.ts`), stub in tests.
- No `self` global under vitest → Azure Blob browser build errors look like REQUEST_SEND_ERROR. Shim `(globalThis as Record<string, unknown>).self = globalThis` (pattern `core/upload/UploadEngine.test.ts`).
- UploadEngine retry counter is per-block — assert retries with a single-block file.
- Session mirror key is `'protoDrop.session'` (SESSION_KEY), NOT `'wa.session'` (HttpOnly cookie). `/auth/me`, `/auth/logout` need `credentials: 'include'`.
- A single vitest failure at feature end is usually a jsdom flake: re-run the file alone, then the full suite; both green = flake. Check whether any feature commit touched that dir.
- Centered fixed/sticky bar: exactly two constraints (`left`+`right`), not left+right+width/margin auto. Verify visually.
- Tab-order assertions must follow computed CSS (global `input[type='file'] { display:none }` removes stops).
- Renaming a class/selector → grep unit tests AND every `e2e/*.spec.ts`.
- New repo-root CI gate script → mirror `scripts/check-*.mjs` (GATE tier exits 1, REPORT tier warns), add a job to `.github/workflows/ci.yml`, and prove it fails (break a token → exit 1 → revert → clean).

## Docs editing
- Docs may be CRLF; edit tools can report misleading results — verify with `git diff` before re-applying an edit (duplicate hunks otherwise).
- PROGRESS.md §1 "Current task" is one giant single line. Anchor edits on a unique full trailing sentence; check uniqueness with `grep -c` first (§2 rows mirror §1 text). Prepend a §2 row by matching only the header + separator lines. Grep every appended marker before committing.
- REPO-FACTS.md must be refreshed in the docs step of any commit that changes a closed set or API shape.

## Feature-end docs generation (po role)
- Template: `F-TRF-009/`. One docs commit per feature. Verify on disk: `grep -c '| ☐ |' spec.md` equals task-file count; spot-read 1–2 task files.

## Push / PR on this machine
- No `gh` CLI. Repo is public (`nl-muhammed-yildirim/ProtoDrop`) — anonymous reads (`git ls-remote`, REST GET) work for triage.
- Headless push can hang on the Git Credential Manager helper-selector dialog; if a push stalls, ask the user to complete the dialog/sign-in, then verify with `git ls-remote origin <branch>`.
- PR merge via REST returned 404 on this repo historically; merges were done by pushing a local `--no-ff` merge commit (`Merge pull request #N from <owner>/<branch>`) to the base ref. Always ask the user before pushing or merging.
- Native Windows tools need `C:/Users/...` paths, not `/c/Users/...`.
