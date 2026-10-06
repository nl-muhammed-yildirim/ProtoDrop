# US-050-02 — Know in one request whether the stack is ready

**Feature:** F-FND-002 — Local Tooling & Health Endpoint | **Status:** done (T-002, 2026-08-31)

---

**Story:** As a developer or CI job starting work against the API, I want one endpoint that tells me whether the database and event backbone are reachable, so that I can distinguish "my code is broken" from "my environment is down".
**Actor:** Developer (manual pass), AI session (exit checks), load balancer/health probes later.
**Goal:** `GET /health` answers with real component status — no fakes.

## Preconditions

- API running locally (port 8080) or in CI.
- Docker data tier up (US-050-01).

## Happy path

1. Client sends `GET /health`.
2. The handler runs a real `SELECT 1` against the configured database.
3. If the `wa` database does not exist, it is created from `master` (dev convenience).
4. Response: 200 with `{ "status": "ok", "db": "ok", "sb": "<ok|skipped>" }`.

## Alternative flows

- **DB down:** 503 + failure reason in the Serilog line; body still Problem-shaped JSON (TA-4.1.3).
- **SB unset locally:** `sb: "skipped"` — Service Bus is optional in Local per AGENT.md §5.2 (in-memory publisher fake used instead).

## Acceptance criteria

```gherkin
Given the sql container is running and azurite is running
When I GET /health
Then the response is 200 with body {"status":"ok","db":"ok","sb":"skipped"}

Given the wa database does not exist on a fresh SQL instance
When I GET /health for the first time
Then the response is 200 and the wa database now exists in master

Given the sql container is stopped
When I GET /health
Then the response is 503 with a Problem+JSON body and a Serilog line explaining the failure
```

## Edge cases

- Health does **not** verify schema — only connectivity (EC-050-4); migrations are separate.
- Azurite down: not checked at M0 (blob checks land with T-007's SAS round-trip test).

## UI notes

- None.

## Technical notes

- Auto-create uses a connection to `master` with the same credentials — dev-only; prod database is Bicep-seeded so this path never runs there.
- The exact 200 body is asserted in T-002's exit check: `{"status":"ok","db":"ok","sb":"skipped"}`.

## Links

- Feature: `F-FND-002-local-tooling-health.md` (FR-050-3, AC-050-1/2/3)
- Architecture: TA-4.1.3 (error shape), TA-13.1 (env vars)
- Milestone: T-002
