# F-TRF-012 — Telemetry & Observability

**Priority:** P0 (foundation) | **Phase:** 0 — Core Transfer
**Spec source:** `02-feature-plan.md` F-TRF-012 | **Architecture:** TA-10
**Milestone tasks:** T-006, T-008, T-025

---

## Description

The foundation every other feature stands on: **telemetry event names are the contract**. Every feature emits the exact events in TA-10.2; a W3C correlation ID follows a request end-to-end (API → Service Bus → Functions → App Insights); and a small set of dashboards + alerts turns raw events into "is everything fine?" before users have to tell us. No secrets, no raw PII, in any event.

**Actors:** operator (dashboards/alerts), developer (correlation lookup), all users (indirectly — every feature depends on this).
**Value:** debuggability and early warning from day one; the cost of adding telemetry later is 10×.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-012-1 | Every feature emits **exactly** the events in **TA-10.2** (names are the contract; the closed list is the set of `upload_started`, `upload_completed`, `upload_failed`, `transfer_page_viewed`, `transfer_not_found`, `password_correct`, `password_wrong`, `download_started`, `download_completed`, `zip_generated`, `zip_failed`, `email_sent`, `email_failed`, `user_created`, `login_success`, `login_failed`, `account_deleted`, `plan_changed`, `translation_missing`, `admin_action`, `dlq_count`). |
| FR-012-2 | Correlation ID: W3C `traceparent` on all requests, propagated end-to-end (API → Service Bus envelope `correlationId` → Functions → child HTTP calls). `CorrelationId` = first 8 hex of the trace-id; echoed in error JSON and logs. |
| FR-012-3 | Application Insights dashboards: **Upload funnel** (started→completed→failed), **Link funnel** (page_viewed→download), **Job health** (expiry/deletion lag), **Email health** (sent/failed), **Business** (transfers/day, storage, plan mix) (TA-10.4). |
| FR-012-4 | Alerts (Azure Monitor, TA-10.3): upload success rate < 99% / 15 min (P1); expiry-job lag > 30 min (P1); email failures > 50/h (P2); DLQ > 0 (P2); 5xx spike > 5% / 10 min (P1); Blob 429 > 10/min (P2). |
| FR-012-5 | PII: no secrets and no full file names in telemetry — file names SHA-256 hashed; IP HMAC(`Wa:Jwt:Secret`); email addresses only in a PII-flagged property bag. Log levels per TA-10.5 (no PII at default level). |

## Acceptance criteria

```gherkin
AC-012-1: Any request fails with a 500
  Then the error JSON carries correlationId
  And Application Insights contains the error under that same id
  And the id is the first 8 hex of the W3C trace-id

AC-012-2: A transfer is created
  Then upload_started, upload_completed, transfer.created-consumed, email_sent events exist
  And all share the same correlationId across API and worker

AC-012-3: An event payload contains a file name
  Then the file name is SHA-256 hashed in the event
  And no raw name or secret appears in any property
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-012-1 | Worker runs without an incoming correlation (timer) | The function generates a trace and uses the job window id as the correlation basis (deterministic per run) |
| EC-012-2 | App Insights down | Telemetry is best-effort (local Serilog continues); no feature logic depends on AI availability |
| EC-012-3 | PII in an event by accident | The lint/review rule: PII properties must be marked `Pii=true` (TA-9.4); CI greps for raw email patterns in event helper calls (T-008 pipeline foundation) |

## UI notes

- No direct user UI (one exception: F-TRF-013 error screens show "Ref: {id8}" — see US-013-01).
- Dashboards live in App Insights (per-environment), not in the product.

## Technical notes

- Envelope: TA-5.2 (`correlationId` field on every Service Bus message); consumers stamp the same id onto their own traces.
- Custom metrics (TA-10.2): `upload.success_rate` (5 min window), `link_to_download_seconds`, `expiry_job_lag_seconds`, `email_failures_1h`, `active_transfers`, `storage_bytes_active`, `api.requests{status}`.
- Serilog configuration (TA-10.5): Information = request/transfer lifecycle, Warning = retries, Error = 5xx/DLQ; PII slots via `{Message:Pii}`.
- SDK: `Serilog.Sinks.ApplicationInsights` (wa.api), App Insights host settings (wa.workers).

## Test plan

- Unit: correlation id derivation (trace-id → 8 hex); PII hashing helpers (file name, IP).
- Integration: endpoint → event with correlationId in payload; replayed message keeps the id; 500 response JSON contains `correlationId` (T-008 exit check).
- E2E: full guest flow → all expected events present in dev AI with matching ids (manual + scripted query).
- Ops: dashboards/alerts exist in dev App Insights (T-025 exit: "All 5 dashboards live in dev App Insights").

## User stories

| ID | Story | File |
|---|---|---|
| US-012-01 | Trace a request end-to-end | `US-012-01-correlation.md` |
| US-012-02 | Monitor funnels with dashboards | `US-012-02-dashboards.md` |
| US-012-03 | Get alerted before users complain | `US-012-03-alerts.md` |
