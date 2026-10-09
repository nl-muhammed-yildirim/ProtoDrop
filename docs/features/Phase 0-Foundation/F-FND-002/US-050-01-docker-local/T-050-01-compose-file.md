# T-050-01 — Add docker-compose.local.yml (sql + azurite)

**Story:** US-050-01 | **Spec:** FR-050-1, AC-050-1 (containers half), EC-050-3 | **Size:** S (one file)
**Depends on:** T-049-10 (F-FND-001 done — solution builds)

---

## Context to read (only these)

- `../../F-FND-002-local-tooling-health.md` → FR-050-1 + Edge cases
- `US-050-01-docker-local.md` → happy path + first Gherkin block

## Instructions

1. Create `docker-compose.local.yml` at repo root with exactly two services:
   - `sql`: SQL Server 2022 (Developer edition), sa/`YourStrong!Passw0rd`, port **1433**
   - `azurite`: blob storage on port **10000**, image **`mcr.microsoft.com/azure-storage/azurite:latest`** (the doc's original `azure-storage:latest` name 404s on MCR — E-002)
2. No volumes at M0 (schema comes from migrations; fresh container + migration run = clean state).

## Exit check

- [x] `docker compose -f docker-compose.local.yml up -d` starts both containers
- [x] `sql` listens on 1433, `azurite` serves blob storage on 10000 (port check)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10, Docker Desktop with WSL2 available).
Task T-050-01 — add the local data tier compose file.
Read first (only): docs/features/Phase 0-Foundation/F-FND-002/F-FND-002-local-tooling-health.md (FR-050-1 + Edge cases) and US-050-01-docker-local.md (happy path + first Gherkin block).
Do exactly:
1. Create docker-compose.local.yml at the repo root with exactly two services:
   - sql: SQL Server 2022 Developer edition, sa/YourStrong!Passw0rd, port 1433.
   - azurite: image mcr.microsoft.com/azure-storage/azurite:latest (NOT azure-storage:latest — that name 404s on MCR), port 10000.
2. No volumes at M0 (schema is created by dotnet ef database update).
3. Run docker compose -f docker-compose.local.yml up -d and confirm both containers are running with the ports bound.
Done when: both containers run — sql on 1433, azurite on 10000 — from this single command.
Constraints: exactly two services; do not add volumes or other images (TA-2.6 / golden rule 1).
```
