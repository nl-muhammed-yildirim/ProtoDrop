# TA-10.4 Dashboards — Dev Environment

These five App Insights dashboards are for **wa-dev-ai only** (dev environment). They are not product UI and not bicep at M0.

## Closed-Set Rule

Every tile query uses only TA-10.2 names from `src/wa.domain/Telemetry/TelemetryEvents.cs`. If you need any other name, that's an ADR-level contract change — not a dashboard fix.

## Timeframes

- 24h / 7d / 30d presets per US-012-02 edge cases
- M0 KQL windows are fixed in the JSON (`ago(24h)`/`ago(7d)`/`ago(30d)`)
- Switch by editing the tile query (dashboard JSON does not support parameterized timeframes at M0)
- **UTC base**: set dashboard timezone to UTC

## Gap Tolerance Rule

Every timeseries query uses `summarize count()/avg() over bin` — a missing event type yields zero/empty rows, never an error tile.

## Dev-Create Procedure

PUT each JSON to wa-dev-ai via az CLI or App Insights REST API (dashboards endpoint under the resource). Name must match TA-10.4 verbatim:

1. Upload funnel
2. Link funnel
3. Job health
4. Email health
5. Business

## Files

| File | Dashboard Name |
|------|----------------|
| `upload-funnel.json` | Upload funnel |
| `link-funnel.json` | Link funnel |
| `job-health.json` | Job health |
| `email-health.json` | Email health |
| `business.json` | Business |
