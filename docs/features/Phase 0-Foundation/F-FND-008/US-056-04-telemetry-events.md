# US-056-04 — Emit telemetry events from a closed, named set

**Feature:** F-FND-008 — Pipeline Foundation (correlation, errors, CORS, rate limits, telemetry) | **Status:** done (T-008d, 2026-09-04)

---

**Story:** As a developer adding the first real upload flow (T-009), I want telemetry events to be emitted through one reporter with names from the closed TA-10.2 set, so that App Insights queries and dashboards can rely on exact template names — no typos, no ad-hoc variants.
**Actor:** Developer (human or AI session), ops (App Insights dashboards at T-025).
**Goal:** `upload_started`-style events emitted with the exact TA-10.2 template names and properties — nothing else.

## Preconditions

- Pipeline foundation in place (T-008d); Serilog wired (F-FND-002 / US-050-03).
- Correlation context active (US-056-01) so events carry the correlationId.

## Happy path

1. A use case emits an event through `WaTelemetryReporter` using a name from `TelemetryEvents.cs`.
2. Serilog writes the line with the exact template name and properties defined in TA-10.2.
3. App Insights (dev) receives it under that exact custom-event name — dashboards at T-025 query by these names.

## Alternative flows

- **A feature needs a new event** → add it to TA-10.2 + `TelemetryEvents.cs` (closed set); ad-hoc names fail review (EC-056-4).
- **Metrics:** custom metrics (`upload.success_rate`, etc.) use the same closed list — emitted by the same pipeline, no separate SDK.

## Acceptance criteria

```gherkin
Given an upload_started-style event emitted through WaTelemetryReporter
When I inspect its Serilog output
Then the template name and properties match TA-10.2 exactly

Given TelemetryEvents.cs
When I list its members
Then every member maps to a name in the closed TA-10.2 set — no extras, no renames
```

## Edge cases

- PII rules apply (TA-9.4): file names → SHA-256, IPs → HMAC — the reporter's property slots are where those transforms live.
- No new packages: built on the existing Serilog context pipeline from T-008a/b (T-008d note).

## UI notes

- None at M0 (dashboards + alerts land with F-TRF-012 / T-025).

## Technical notes

- TA-10.2 is the contract: events (`upload_started`, `transfer_page_viewed`, …) and metrics (`api.requests{status}`, …) — "names are code."
- This is the M0 half of F-TRF-012: contract now, dashboards/alerts at T-025.

## Links

- Feature: `F-FND-008-pipeline-foundation.md` (FR-056-6, AC-056-4)
- Architecture: TA-10.2, TA-9.4, TA-10.5
- Milestone: T-008d
