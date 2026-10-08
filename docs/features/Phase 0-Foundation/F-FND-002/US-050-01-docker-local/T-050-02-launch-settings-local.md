# T-050-02 — Add the Local profile with every §5.2 env var

**Story:** US-050-01 | **Spec:** FR-050-2, AC-050-1 (SELECT 1 half), EC-050-3 | **Size:** S (one file)
**Depends on:** T-050-01

---

## Context to read (only these)

- `../../F-FND-002-local-tooling-health.md` → FR-050-2, FR-050-5 + Technical notes
- `US-050-01-docker-local.md` → second Gherkin block
- `docs/AGENT.md` (repo root docs) → §5.2 only

## Instructions

1. Add a `Local` profile to `src/wa.api/Properties/launchSettings.json` with **every** env var from AGENT.md §5.2: DB connection string (sa on 1433), Azurite account URL + key, optional SB/Communication Hub (unset = fakes), JWT secret (64 hex chars), public/API URLs, CORS origins, admin emails, zip signing key.
2. API listens on port **8080** locally (`applicationUrl`).

## Exit check

- [x] `dotnet run --launch-profile Local` in `src/wa.api` starts without missing-config errors
- [x] `SELECT 1` succeeds against the `wa` database using the profile's connection string (manual or script)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; docker-compose.local.yml already up: sql on 1433, azurite on 10000).
Task T-050-02 — configure the Local launch profile.
Read first (only): docs/features/Phase 0-Foundation/F-FND-002/F-FND-002-local-tooling-health.md (FR-050-2, FR-050-5) and docs/AGENT.md §5.2 only.
Do exactly:
1. In src/wa.api/Properties/launchSettings.json add a profile named "Local" with every env var from AGENT.md §5.2: DB connection string (sa/YourStrong!Passw0rd on localhost,1433), Azurite account URL + key (localhost:10000), optional SB/Communication Hub left unset (fakes are used when unset), JWT secret (64 hex chars), public/API URLs, CORS origins, admin emails, zip signing key.
2. Set the API's local applicationUrl to port 8080.
3. Run dotnet run --launch-profile Local in src/wa.api and confirm it starts without missing-config errors; then verify SELECT 1 succeeds against the wa database with that connection string (the db may not exist yet — that is created later by /health, so for this check create it manually or just validate the connection to master).
Done when: the Local profile contains every §5.2 variable and the API starts on port 8080 against the Docker data tier.
Constraints: no secrets in code/appsettings.json — launchSettings is the local home (TA-13.1); do not add new packages.
```
