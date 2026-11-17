# docs/alerts — TA-10.3 alert rules (T-012-04)

The six TA-10.3 alerts wired in dev as Azure Monitor rule JSON. Committed as the auditable record (same precedent as `docs/dashboards/` from T-012-03); no bicep at M0 — create via az CLI / AI REST API per the procedure below.

## 1. Exactly six alerts (alert fatigue guard)

| Rule file | Name | Severity | Condition (TA-10.3 verbatim) | Window |
|---|---|---|---|---|
| `upload-success-low.json` | Upload success low | 0 (P1) | upload.success_rate < 0.99 for 15 min | 15 min |
| `expiry-lag.json` | Expiry lag | 0 (P1) | expiry_job_lag_seconds > 1800 s | 5 min eval, 1800 s threshold |
| `email-failures.json` | Email failures | 1 (P2) | email_failures_1h > 50/h | 60 min window, PT5M eval |
| `dlq.json` | DLQ | 1 (P2) | dlq_count > 0 | 5-min count |
| `5xx-spike.json` | 5xx spike | 0 (P1) | > 5% of requests over 10 min | 10 min |
| `blob-429s.json` | Blob 429s | 1 (P2) | storage 429 rate > 10/min | 1 min |

No translation_missing or PII alerts — US-012-03 Alternative flows forbid them. Severities stay verbatim per TA-10.3 in prod; dev is informational via the rule description suffix `dev: informational (launch traffic is synthetic)` on all six. metricAlerts/scheduledQueryRules have no custom tags property at M0 — **the description IS the tag**; do not add a tags block.

## 2. Rule-type split and why

- **Log-search (`Microsoft.Insights/scheduledQueryRules@2021-08-01`)**: Upload success low, DLQ, 5xx spike — their inputs are closed customEvents (TA-10.2 names from `src/wa.domain/Telemetry/TelemetryEvents.cs`) or built-in AI request telemetry (`requests`/`resultCode` via TA-10.5 Serilog → AI), not pre-aggregated metrics. The 2021-08-01 shape uses `criteria.allOf[]` with a per-condition `query`, ISO-8601 `evaluationFrequency`/`windowSize`, and (for the rate/count rules) `metricMeasureColumn` pointing at the computed column — without it, `timeAggregation: Count` measures row count (= 1 after `summarize`) instead of the value.
- **Metric alerts (`Microsoft.Insights/metricAlerts@2018-03-01`)**: Expiry lag, Email failures — custom metrics under the `Azure.ApplicationInsights` namespace (T-056-04 reporter); Blob 429s — platform storage metric `Transactions` on `Microsoft.Storage/storageAccounts`, filtered by the ResponseType dimension to the 429 throttling values (`ClientAccountRequestThrottlingError`, `ClientAccountBandwidthThrottlingError`, `ClientThrottlingError`; OR across values per the dimensions schema). There is no `FailedRequests` platform metric — that name only exists as a column in the classic `$metrics` Storage Analytics table.

Note: `upload.success_rate` has no code-side emitter at M0 — the Upload success low log query over `upload_started`/`upload_completed` covers it per the task file DECISION. If unworkable in dev, escalate (AGENT.md §8); do not add an emitter silently.

## 3. Windows rule

Every evaluation window equals the TA-10.3 value verbatim (15 min upload, 1800 s expiry, 50/h email over 60 min, 10 min 5xx, 1/min blob; DLQ 5-min count) — no divergence between "what I see" and "what paged me" (US-012-03 Edge cases).

## 4. Dev-create procedure (az CLI — same auth surface as T-012-03's dashboard create)

No local az CLI at plan time: install it or use the AI REST API. No bicep at M0.

a) **Action group first** (rules reference its id):
```
az monitor action-group create --resource-group rg-wa-dev --name wa-dev-alert-emails --location global --email-recipients <operator>
```
b) **Metric alerts**:
```
az monitor metrics alert create --resource-group rg-wa-dev --scopes "<scope>" --name "<rule name>" --condition "<metricName> > <threshold>" --evaluation-period <window> --severity <0|1> --action <action-group-id>
```
c) **Log-search rules** (no dedicated az command for scheduledQueryRules):
```
az rest --put --url /subscriptions/{SUB}/resourceGroups/rg-wa-dev/providers/Microsoft.Insights/scheduledQueryRules/<rule name> --body "@<file>.json"
```
Fill `{SUB}` + scope ids first via `az account show` / `az resource list -g rg-wa-dev`.

d) **Dashboard link**: after each rule exists, set the portal "Link to dashboard" on it (portal → alert rule → Linked resources/visualizations; stored in properties.linkedVisualizations and rendered as the email's "View details" URL). Mapping below.

## 5. Link map (US-012-03 happy path 2 + task file item 2)

| Rule | Dashboard | Dashboard id (from docs/dashboards/*.json) |
|---|---|---|
| Upload success low | Upload funnel | `8ac05f38-5050-4c80-a55e-aad3f814f309` |
| Expiry lag | Job health | `f1c8ca79-7c78-4aad-a512-6051a3bfb327` |
| Email failures | Email health | `f01b07ee-a968-460f-a180-bf9a2cf7fe70` |
| DLQ | Job health | `f1c8ca79-7c78-4aad-a512-6051a3bfb327` |
| 5xx spike | Business (no dedicated API-health dashboard in TA-10.4 — closest live tile) | `3e32757d-996d-4142-a9f6-33cf2e56d051` |
| Blob 429s | **none** (task file permits "Business or none"; choice: none, recorded here) | — |

## 6. Fill-in list (placeholders at commit time — the JSONs remain the auditable record)

- `{SUB}` subscription id
- wa-dev-ai component id: `/subscriptions/{SUB}/resourceGroups/rg-wa-dev/providers/Microsoft.Insights/components/wa-dev-ai`
- Dev blob account id + name: `/subscriptions/{SUB}/resourceGroups/rg-wa-dev/providers/Microsoft.Storage/storageAccounts/wa-dev-blob` (confirm actual dev storage name via `az storage account list --resource-group rg-wa-dev` — TA-11.1 says wa-{env}-blob)
- Operator email (action group recipient — no secrets in repo; fill at create)

## 7. Documented deviations / fixes vs plan

- **Upload success low**: log-search rule instead of a metric alert — no `upload.success_rate` emitter at M0 (see §2).
- **Log-search rules use apiVersion 2021-08-01** (plan pinned 2016-03-30): the flat top-level `query` + `conditions[]` shape documented for that version measures row count with `timeAggregation: Count` (= 1 after a single-row `summarize`), so rate rules would evaluate as "1 < 99" (always fires) or "1 > 5" (never fires). The modern shape gives each condition its own query + `metricMeasureColumn`, fixing all three rules in one pass. Microsoft's RM template page documents only 2021-08-01 and 2025-01-01-preview as templates, and the REST reference for 2016-03-30 404s (documented legacy versions are 2016-03-01 / 2018-04-16).
- **Blob 429s metric**: `Transactions` + ResponseType filter instead of the plan's `FailedRequests` — no platform metric named FailedRequests exists in Microsoft.Storage/storageAccounts (that name is a column in the classic `$metrics` Storage Analytics table); Transactions is the documented throttling metric (monitor-blob-storage-reference).
