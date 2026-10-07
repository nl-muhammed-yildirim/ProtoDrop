# US-050-03 — See what happened without opening a debugger

**Feature:** F-FND-002 — Local Tooling & Health Endpoint | **Status:** done (T-002, 2026-08-31)

---

**Story:** As a developer debugging locally, I want every request and failure to produce structured Serilog lines in the terminal, so that I can read what happened without attaching a debugger.
**Actor:** Developer (human or AI session), working on any task after T-002.
**Goal:** Logging is wired once at M0; every later task inherits readable logs.

## Preconditions

- API running with the Local profile.

## Happy path

1. A request comes in and completes (or fails).
2. Serilog writes structured console lines: level, timestamp, message.
3. `Microsoft.*` framework noise is suppressed to Warning so the developer's own events stand out.

## Alternative flows

- **Failure:** exception line includes the reason; no stack-trace leak into responses (that's T-008b's Problem+JSON job — logs keep the detail).

## Acceptance criteria

```gherkin
Given the API is running with Serilog console wiring
When any request completes or fails
Then a Serilog line appears in the terminal with level, timestamp, and message
And Microsoft.* framework logging is at Warning (not Information)

Given a /health call on a fresh setup
When it succeeds
Then INF lines are visible in the terminal (T-002 exit check)
```

## Edge cases

- PII in logs: TA-9.4 rules apply from day one — no raw IPs, no file names in event payloads without hashing.
- Log volume: console sink only locally; App Insights sink is added per environment later (TA-10.1).

## UI notes

- None.

## Technical notes

- Levels per TA-10.5. The wiring lives in `wa.api/Program.cs` — the composition root owns host-level concerns.
- This story is the local half of F-TRF-012 (Telemetry & Observability); the App Insights resource and dashboards are T-025's scope.

## Links

- Feature: `F-FND-002-local-tooling-health.md` (FR-050-4, AC-050-4)
- Architecture: TA-10.1, TA-10.5
- Milestone: T-002
