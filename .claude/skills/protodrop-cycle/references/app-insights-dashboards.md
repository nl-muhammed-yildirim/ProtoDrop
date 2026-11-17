# Dev App Insights environment + dashboard JSON recipe (F-TRF-012 dev-environment tasks)

Tasks that commit dashboards/alerts to dev App Insights are DEV-ENVIRONMENT tasks, not code changes: the deliverable is committed JSON blobs under `docs/dashboards/` (+ a README recording conventions), and live creation in the cloud resource happens as a follow-up step because no local Azure auth surface exists. The file-level contract (valid AI dashboard JSON + closed-set purity) is what the orchestrator verifies before review; AC blocks requiring live data are verified after the dashboards exist.

## Environment facts (verified 2026-11)
- Dev AI resource: `wa-dev-ai` (TA-11.1 naming `wa-{env}-{purpose}`, rg `rg-wa-dev`). Cloud — no local equivalent (blob has Azurite in docker-compose.local.yml).
- No `az` CLI on this machine (`where.exe az` → nothing); `~/.azure/` holds only vscode extension state; no service-principal config.
- No AI connection string in any local config: appsettings.json, appsettings.Development.json, launchSettings.json, docker-compose.local.yml all lack `Wa:AppInsights:ConnectionString` — it is a CI-injected KV ref per TA-13.1.
- No committed infra/bicep/ dir at M0 (TA-10.5 references `infra/bicep/main/{env}.bicep`, not yet present) → reject a CI job for dashboard creation; create via az CLI locally or one-time portal paste with the JSON as the auditable record.
- T-012-04 (alerts) needs these dashboards to exist first — alerts link to their tiles — and also real Azure access + the dev blob account.

## Dashboard JSON schema (verified against commit e8db708)
One dashboard = one file, top-level `{ "name", "id" (GUID), "items": [...] }`. `name` must match TA-10.4 verbatim ("Upload funnel", "Link funnel", "Job health", "Email health", "Business"). Each tile item:

```
{
  "type": "ApplicationInsights.MsQuotedVersionedDashboardItem",
  "id": "<unique GUID>",
  "name": "<tile name>",
  "position": { "x": 0, "y": 0, "colSpan": 6, "rowSpan": 4 },
  "metadata": { "colors": {} },
  "data": {
    "baseType": "Timeseries" | "Number" | "Text",
    "type": "applicationInsightsEntityData",
    "query": "<KQL>",          // omitted for Text tiles — use "text" instead
    "title": "<tile title>"
  }
}
```

- Timeseries/Number: `data.type = "applicationInsightsEntityData"` + KQL in `data.query`.
- Text (placeholder) tile: no query; put the string under `data.text` (e.g. a P1-ready plan-mix placeholder).
- Validate each file parses (`python -c "import json; json.load(open(...))"`) — coders can report `{}` (empty), so verify on disk.

## KQL conventions
- In classic AI (wa-dev-ai queried directly), both `customEvents` and `customMetrics` expose a **`timestamp`** column — there is NO `time` column. Do NOT write `bin(time, …)` or add a `project timestamp = time` workaround: those were the T-012-03 bug (all five dashboards fixed to `bin(timestamp, 1h)`, amended commit). customMetrics also has a `value` column.
- Count over time: `customEvents | where name in ('a','b') | summarize count() by bin(time, 1h), name`.
- Number tiles compute in-query (success rate via `count_if` + a `max(started, 1)` divide-by-zero guard; conversion % the same way). Do NOT use the alert metrics for funnel numbers — those are T-012-04's concern; cite that split in the tile title.
- Threshold marker line: separate item with a flat reference query so the tile visibly approaches red as AC expects: `range t in range(ago(24h), now(), 1h) | extend value = 1800.0 | project timestamp = t, value` (title cites the TA-10.3 row).
- Windows are UTC; gap tolerance — a missing event renders zero/empty, not an error tile. M0 gaps (link_to_download_seconds, active_transfers, plan mix) render empty until their emitting task lands; note that in the tile title.

## Closed-set purity check (post-commit, programmatic — do not eyeball)
Extract every quoted literal from every KQL query and diff against `src/wa.domain/Telemetry/TelemetryEvents.cs` members:

```
members = set(re.findall(r'"([a-z0-9_.]+)"', open('src/wa.domain/Telemetry/TelemetryEvents.cs').read()))
names   = set(re.findall(r"'([a-z0-9_.]+)'", query))  # per KQL query
bad     = names - members
```

Every KQL name must be a literal member (TA-10.2 "names are code"). verify_plan.py covers this at plan time; the post-commit diff is the second line of defense.

## M0 AI-sink gap (load-bearing for all F-TRF-012 dashboards)
Server events are emitted as Serilog `LogInformation` with template = event name (`WaTelemetryReporter.cs`); Program.cs writes to Console only at M0 ("App Insights later per environment") and TA-10.5 says log streaming Container Apps → App Insights. The query surface is pinned by US-056-04: "App Insights (dev) receives it under that exact custom-event name — dashboards at T-25 query by these names." So KQL targets `customEvents`/`customMetrics` per spec; whether dev server lines actually land as customEvents is a live-dev verification outcome. If they surface as traces instead, rewrite queries against the `traces` table (`message startswith '<event>'`) — same closed-set names. Client events (T-012-02 sink) POST to TrackEvents = true customEvents, so browser-originated tiles are safe either way.

## Gates for docs-only commits
A docs-only diff still runs the full gate set; .NET fast gates + JSON parse per file. Web gate: a docs-only diff cannot cause web test failures — if any appear, triage against the last feature commit (a new import inside a telemetry module can break jsdom render paths that call it), not this one.
