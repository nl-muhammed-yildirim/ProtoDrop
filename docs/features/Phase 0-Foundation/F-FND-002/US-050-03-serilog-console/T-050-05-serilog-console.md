# T-050-05 — Wire Serilog console logging with TA-10.5 levels

**Story:** US-050-03 | **Spec:** FR-050-4, AC-050-4 | **Size:** S (Program.cs + one package line)
**Depends on:** T-050-04

---

## Context to read (only these)

- `../../F-FND-002-local-tooling-health.md` → FR-050-4 + Technical notes (Serilog lines only)
- `US-050-03-serilog-console.md` → happy path + Gherkin blocks

## Instructions

1. Add Serilog to `wa.api` (package line needs the ADR note per golden rule 1 — TA-2.6 allows it for M0 tooling).
2. Wire in `Program.cs`: console sink, levels per **TA-10.5** — `Microsoft.*` → **Warning**, everything else readable at Information.
3. Every request and failure must produce a structured line: level, timestamp, message. No PII beyond TA-9.4 rules (no raw IPs, no file names in event payloads without hashing).

## Exit check

- [x] Any request to the API produces Serilog console lines with level + timestamp + message
- [x] `Microsoft.*` framework logging is at Warning, not Information
- [x] A `/health` call on a fresh setup shows INF lines (T-002 exit check)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; GET /health green including the 503 path).
Task T-050-05 — wire Serilog console logging.
Read first (only): docs/features/Phase 0-Foundation/F-FND-002/F-FND-002-local-tooling-health.md (FR-050-4 + Technical notes) and US-050-03-serilog-console.md (happy path + Gherkin blocks).
Do exactly:
1. Add the Serilog packages to src/wa.api (Serilog.AspNetCore, Serilog.Sinks.Console — ADR line per golden rule 1; TA-2.6 allows them for M0 tooling).
2. In Program.cs wire the console sink with levels per TA-10.5: Microsoft.* → Warning; app logging at Information.
3. Verify: any request produces structured console lines (level, timestamp, message); a /health call on a fresh setup shows INF lines.
Done when: every request and failure logs a readable Serilog line in the terminal, Microsoft.* is suppressed to Warning, and no PII leaks beyond TA-9.4 rules.
Constraints: composition root only — wiring lives in wa.api/Program.cs; console sink only locally (App Insights sink comes later per environment).
```
