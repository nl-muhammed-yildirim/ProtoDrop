# T-056-04 — TelemetryEvents.cs + WaTelemetryReporter (closed TA-10.2 set)

**Story:** US-056-04 | **Spec:** FR-056-6, AC-056-4, EC-056-4 | **Size:** M
**Depends on:** T-056-01 (correlationId in the log context — events carry it)

---

## Context to read (only these)

- `US-056-04-telemetry-events.md` → happy path + Gherkin blocks + Edge cases
- `../../F-FND-008-pipeline-foundation.md` → FR-056-6 + AC-056-4 + EC-056-4 + Technical notes (T-008d line)

## Instructions

1. Add **`TelemetryEvents.cs`** encoding the closed TA-10.2 event/metric/key set — every member maps to a name in the closed set, no extras, no renames.
2. Add **`WaTelemetryReporter`** emitting Serilog events with exact template names and properties (e.g. `upload_started`) — no new packages; built on the T-056-01/02 Serilog context pipeline.
3. PII rules apply (TA-9.4): file names → SHA-256, IPs → HMAC — the reporter's property slots are where those transforms live.

## Exit check

- [ ] An `upload_started`-style event emitted through WaTelemetryReporter produces Serilog output whose template name and properties match TA-10.2 exactly
- [ ] Listing TelemetryEvents.cs members shows a 1:1 mapping to the closed TA-10.2 set (no ad-hoc names)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; Serilog context pipeline in place per T-056-01/02).
Task T-056-04 — add the telemetry contract.
Read first (only): docs/features/Phase 0-Foundation/F-FND-008/US-056-04-telemetry-events/US-056-04-telemetry-events.md (happy path + Gherkin blocks + Edge cases) and F-FND-008-pipeline-foundation.md (FR-056-6).
Do exactly:
1. Add TelemetryEvents.cs encoding the closed TA-10.2 event/metric/key set — every member maps to a name in the closed set, no extras, no renames.
2. Add WaTelemetryReporter emitting Serilog events with exact template names and properties (e.g. upload_started) — no new packages; built on the existing Serilog context pipeline.
3. Apply the PII rules (TA-9.4): file names → SHA-256, IPs → HMAC — the reporter's property slots are where those transforms live.
Done when: AC-056-4 holds — an upload_started-style event emits with the exact TA-10.2 template name and properties, and TelemetryEvents.cs is a 1:1 map of the closed set.
Constraints: "names are code" (TA-10.2) — a new event name requires adding it to TA-10.2 + TelemetryEvents.cs (EC-056-4); ad-hoc names fail review; no separate metrics SDK — same pipeline.
```
