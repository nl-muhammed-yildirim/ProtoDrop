# T-050-03 — GET /health with real DB ping and auto-created wa database

**Story:** US-050-02 | **Spec:** FR-050-3, AC-050-1/2, EC-050-4 | **Size:** M
**Depends on:** T-050-02

---

## Context to read (only these)

- `../../F-FND-002-local-tooling-health.md` → FR-050-3 + Technical notes
- `US-050-02-health-check.md` → happy path + first two Gherkin blocks

## Instructions

1. Add `GET /health` to `wa.api`:
   - real `SELECT 1` against the configured database (no fakes)
   - if the `wa` database does not exist, auto-create it from `master` with the same credentials (dev-only convenience — prod is Bicep-seeded)
   - Service Bus: report `"skipped"` when its connection string is unset; report failure when set but unreachable
2. Response on success: **exactly** `200 {"status":"ok","db":"ok","sb":"skipped"}` (SB unset locally).

## Exit check

- [x] Fresh SQL instance → first `GET /health` returns 200 and the `wa` database now exists in master
- [x] Body is byte-exact: `{"status":"ok","db":"ok","sb":"skipped"}` (AC-050-1)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10, Local profile configured; sql container on 1433).
Task T-050-03 — implement GET /health.
Read first (only): docs/features/Phase 0-Foundation/F-FND-002/F-FND-002-local-tooling-health.md (FR-050-3 + Technical notes) and US-050-02-health-check.md (happy path + first two Gherkin blocks).
Do exactly:
1. Add GET /health to wa.api that runs a real SELECT 1 against the configured database (no fakes).
2. If the "wa" database does not exist, auto-create it from master using the same credentials (dev-only; prod is Bicep-seeded — never verify schema here).
3. Service Bus reporting: "skipped" when its connection string is unset; report failure when set but unreachable.
4. On success return exactly 200 with body {"status":"ok","db":"ok","sb":"skipped"} (SB unset locally).
Done when: on a fresh SQL instance the first GET /health returns 200 with that exact body AND the wa database now exists in master.
Constraints: health pings connectivity only — it does NOT verify schema (EC-050-4); no new packages beyond what TA-2.6 allows.
```
