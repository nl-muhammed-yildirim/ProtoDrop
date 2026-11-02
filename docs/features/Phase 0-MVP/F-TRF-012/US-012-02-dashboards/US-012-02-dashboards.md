# US-012-02 — Monitor funnels with dashboards

**Feature:** F-TRF-012 — Telemetry & Observability | **Status:** pending

---

**Story:** As the operator, I want the five dashboards (upload funnel, link funnel, job health, email health, business) to exist in App Insights, so that "how is the product doing?" has a screen, not a guess.
**Actor:** Operator.
**Goal:** Five dashboards, live in every environment from the start.

## Preconditions

- Telemetry events flowing per FR-012-1 (the events are the contract, TA-10.2).

## Happy path

1. **Upload funnel:** started → completed → failed over time, with success rate (the <99% alert is computed from this).
2. **Link funnel:** `transfer_page_viewed` → `download_started` → `download_completed`; conversion % and `link_to_download_seconds`.
3. **Job health:** `expiry_job_lag_seconds` + deletion job lag; red when the >30 min alert trips.
4. **Email health:** `email.sent` vs `email.failed` per hour; DLQ count.
5. **Business:** transfers/day, storage active (`storage_bytes_active`), active transfers, plan mix (P1-ready).

## Alternative flows

- **Dev environment:** same dashboards against dev App Insights — the dev pass is where they're built and verified (T-025 exit: "All 5 dashboards live in dev App Insights").
- **Data gap:** a missing event type shows as a zero, not a broken tile (queries tolerate gaps).

## Acceptance criteria

```gherkin
Given the five dashboards exist in dev App Insights
When I send a test transfer and a test download
Then the upload funnel and link funnel both reflect the events within 5 minutes
And no tile shows an error state

Given expiry is delayed artificially (flag RETENTION_DAYS=0 + paused job)
When I open Job health
Then the lag line rises toward the alert threshold
```

## Edge cases

- Dashboard queries are read-side only — no dashboard writes to the product.
- Timeframe: 24 h / 7 d / 30 d presets; UTC time base.

## UI notes

- App Insights dashboards (not product UI); named per TA-10.4: "Upload funnel", "Link funnel", "Job health", "Email health", "Business".

## Technical notes

- Built from the closed event/metric list (TA-10.2) — a new dashboard need = a new event = a contract change (ADR-level).
- Custom metrics: `upload.success_rate`, `link_to_download_seconds`, `expiry_job_lag_seconds`, `email_failures_1h`, `active_transfers`, `storage_bytes_active`, `api.requests{status}`.

## Links

- Feature: `TRF-012-telemetry.md` (FR-012-3)
- Architecture: TA-10.4, TA-10.2
- Milestone: T-025
