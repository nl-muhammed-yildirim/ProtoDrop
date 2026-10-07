# US-050-01 — Bring up my local data tier with one command

**Feature:** F-FND-002 — Local Tooling & Health Endpoint | **Status:** done (T-002, 2026-08-31)

---

**Story:** As a developer setting up ProtoDrop on a new machine, I want to start SQL Server and blob storage with one command, so that I never have to install or configure either product manually.
**Actor:** Developer (human or AI session), with Docker Desktop (WSL2) installed.
**Goal:** A local data tier identical in shape to dev/staging/prod: real SQL Server 2022 + Azurite blob storage.

## Preconditions

- Docker Desktop running (WSL2 backend).
- Repo cloned; `docker-compose.local.yml` present at repo root.

## Happy path

1. Developer runs `docker compose -f docker-compose.local.yml up -d`.
2. Both containers start: `sql` on 1433, `azurite` on 10000.
3. The API's Local profile (launchSettings) points at them via the §5.2 env vars.

## Alternative flows

- **Tear down:** `docker compose -f docker-compose.local.yml down` — data persists in volumes only if defined; M0 uses ephemeral containers, schema comes from migrations.
- **CI equivalent:** integration tests use Testcontainers with the same images (TA-14.3), so local and CI behavior match.

## Acceptance criteria

```gherkin
Given Docker Desktop is running
When I run docker compose -f docker-compose.local.yml up -d
Then a container named sql listens on port 1433 (SQL Server 2022, sa/YourStrong!Passw0rd)
And a container named azurite serves blob storage on port 10000

Given both containers are running
When I connect with the launchSettings Local profile connection string
Then SELECT 1 succeeds against the wa database
```

## Edge cases

- Azurite image: the doc's original `azure-storage:latest` name 404s on MCR — the compose file uses the real `mcr.microsoft.com/azure-storage/azurite:latest` (E-002).
- Ports already in use: Docker reports the conflict; free 1433/10000 or adjust (EC-050-3).

## UI notes

- None.

## Technical notes

- Compose file is verbatim AGENT.md §5.1 except the Azurite image fix (E-002) — keep it in sync with the doc when either changes.
- No volumes at M0: schema is created by `dotnet ef database update` (AGENT.md §5.3), so a fresh container + migration run = clean state.

## Links

- Feature: `F-FND-002-local-tooling-health.md` (FR-050-1, FR-050-2)
- Architecture: TA-4.1 (health shape), TA-13.1 (env vars)
- Milestone: T-002
