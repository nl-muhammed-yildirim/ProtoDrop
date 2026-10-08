# F-FND-002 — Local Tooling & Health Endpoint

**Priority:** P0 (foundation) | **Phase:** 0 — Foundation (M0)
**Spec source:** `Milestone-Backlog.md` T-002, AGENT.md §5 | **Architecture:** TA-4.1, TA-13.1
**Milestone tasks:** T-002

---

## Description

Every developer and AI session needs the same local environment: SQL Server 2022 + Azurite in Docker, a fixed set of env vars, and a single endpoint that answers "is this machine ready?" `GET /health` pings the real database (auto-creating the missing `wa` database on first run) and reports Service Bus availability; Serilog wires structured logging from day one so every later task's debugging story has logs to read. This is the foundation feature behind AGENT.md §5 — the local runbook becomes executable code instead of prose.

**Actors:** developer (human or AI session), CI (integration tests spin up the same containers via Testcontainers).
**Value:** "it works on my machine" becomes "it works in Docker, everywhere"; a 200 from `/health` is the precondition for every later manual pass.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-050-1 | `docker-compose.local.yml` at repo root defines exactly two services per AGENT.md §5.1: `sql` (SQL Server 2022, sa/YourStrong!Passw0rd, Developer edition, port 1433) and `azurite` (Azurite blob on port 10000). |
| FR-050-2 | `wa.api/Properties/launchSettings.json` has a `Local` profile with every env var in AGENT.md §5.2: DB connection string, Azurite account URL + key, optional SB/Communication Hub (unset = fakes), JWT secret (64 hex chars), public/API URLs, CORS origins, admin emails, zip signing key. |
| FR-050-3 | `GET /health` returns 200 with JSON `{ "status": "ok", "db": "ok", "sb": "<ok|skipped>" }`: the DB check is a **real** `SELECT 1`; if the `wa` database does not exist it is auto-created from `master` (dev convenience); on failure the endpoint returns 503. Service Bus reports `"skipped"` when its connection string is unset (per §5.2, SB is optional locally). |
| FR-050-4 | Serilog is wired in `Program.cs`: console sink, levels per TA-10.5 (`Microsoft.*` → Warning), so every request and failure produces structured lines the developer can read in the terminal. |
| FR-050-5 | The API listens on port **8080** locally (the value CORS and `Wa:Url:Api` depend on). |

## Acceptance criteria

```gherkin
AC-050-1: Docker is up, first run
  Given docker compose -f docker-compose.local.yml up -d has completed
  When I start wa.api with the Local profile and GET /health
  Then the response is 200 {"status":"ok","db":"ok","sb":"skipped"}

AC-050-2: The database really exists
  Given a first successful /health on an empty SQL instance
  When I query master for databases
  Then a database named "wa" exists (auto-created)

AC-050-3: DB down is visible
  Given the sql container is stopped
  When I GET /health
  Then the response is 503 and Serilog shows the failure reason

AC-050-4: Logs are readable
  Given any request to the API
  When it completes or fails
  Then Serilog console lines appear with level, timestamp, and message (no PII beyond TA-9.4 rules)
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-050-1 | Azurite image name in the doc 404s on MCR (`azure-storage:latest`) | Real image `mcr.microsoft.com/azure-storage/azurite:latest` used (E-002); command line kept per §5.1 |
| EC-050-2 | SB connection string set but broker unreachable | `/health` reports the failure (503) — locally SB is optional, so unset is preferred over broken |
| EC-050-3 | Port 8080 or 1433 in use | Docker/launchSettings error surfaces; documented ports are canonical (AGENTS.md §5.2) |
| EC-050-4 | `wa` db exists but schema is missing | `/health` still 200 (it pings, it does not verify schema); migrations are a separate command (AGENT.md §5.3) |

## UI notes

- None (API + tooling only).

## Technical notes

- Auto-create of the `wa` db: connect to `master`, `CREATE DATABASE wa` if absent — dev-only convenience, never in prod (prod is Bicep-seeded).
- SB/Communication Hub are **optional locally**: unset connection strings mean the in-memory publisher fake / log-only sender are used (T-006/T-019 build on this).
- Serilog levels per TA-10.5; `Microsoft.*` → Warning to keep the terminal readable.

## Test plan

- Manual pass: start Docker, run API, hit `/health` — 200 body exact (exit check T-002).
- Integration: DB-ping failure path covered by stopping the container and asserting 503 (manual or test).
- Gate: full AGENT.md §4 gate green.

## User stories & implementation tasks

| ID | Story / Task | File |
|---|---|---|
| US-050-01 | Bring up my local data tier with one command | `US-050-01-docker-local/US-050-01-docker-local.md` |
| US-050-02 | Know in one request whether the stack is ready | `US-050-02-health-check/US-050-02-health-check.md` |
| US-050-03 | See what happened without opening a debugger | `US-050-03-serilog-console/US-050-03-serilog-console.md` |

**Implementation tasks:** one file per task — each story folder holds its story .md + its task files (context-friendly; execute top-to-bottom).

| Story | Task | File | Status |
|---|---|---|---|
| US-050-01 | T-050-01 Add docker-compose.local.yml (sql + azurite) | `US-050-01-docker-local/T-050-01-compose-file.md` | ☑ |
| US-050-01 | T-050-02 Add the Local profile with every §5.2 env var | `US-050-01-docker-local/T-050-02-launch-settings-local.md` | ☑ |
| US-050-02 | T-050-03 GET /health with real DB ping and auto-created wa database | `US-050-02-health-check/T-050-03-health-endpoint.md` | ☑ |
| US-050-02 | T-050-04 DB down returns 503 with Problem+JSON body | `US-050-02-health-check/T-050-04-db-down-failure-path.md` | ☐ |
| US-050-03 | T-050-05 Wire Serilog console logging with TA-10.5 levels | `US-050-03-serilog-console/T-050-05-serilog-console.md` | ☐ |

**Story done when:** all tasks checked + full AGENT.md §4 gate green + the story's ACs verified. Then T-002 can be marked `done` in `Milestone-Backlog.md`.
