# T-050-04 — DB down returns 503 with Problem+JSON body

**Story:** US-050-02 | **Spec:** AC-050-3, EC-050-2/4 | **Size:** S
**Depends on:** T-050-03

---

## Context to read (only these)

- `US-050-02-health-check.md` → Alternative flows + third Gherkin block
- `../../F-FND-002-local-tooling-health.md` → AC-050-3 + Edge cases

## Instructions

1. When the database is unreachable, `GET /health` returns **503** with a Problem-shaped JSON body (TA-4.1.3) — not an HTML error page, not a raw exception.
2. The failure reason goes to the log line (the Serilog wiring lands in T-050-05; until then the console must still show the reason).

## Exit check

- [x] With the `sql` container stopped: `GET /health` → 503 + Problem+JSON body
- [x] Failure reason visible in the terminal (re-verify after T-050-05 for the Serilog line)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; GET /health already implemented, happy path green).
Task T-050-04 — make the DB-down failure path clean.
Read first (only): docs/features/Phase 0-Foundation/F-FND-002/US-050-02-health-check/US-050-02-health-check.md (Alternative flows + third Gherkin block).
Do exactly:
1. When the database is unreachable, GET /health must return 503 with a Problem-shaped JSON body (TA-4.1.3) — no HTML error page, no raw exception leak.
2. Log the failure reason to the console (full Serilog line arrives with T-050-05).
Done when: stopping the sql container makes GET /health return 503 with a Problem+JSON body and the failure reason visible in the terminal.
Constraints: keep the happy-path response byte-exact — do not change the 200 body; health still pings connectivity only (no schema check).
```
