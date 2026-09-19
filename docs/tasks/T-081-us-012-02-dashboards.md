# T-081 — Monitor funnels with dashboards

**Story:** US-012-02 | **Feature:** F-TRF-012 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-012/US-012-02-dashboards.md`
**Coarse task (Milestone-Backlog.md):** T-025
**Status:** pending

---

## Scope

As the operator, I want the five dashboards (upload funnel, link funnel, job health, email health, business) to exist in App Insights, so that "how is the product doing?" has a screen, not a guess.

**Actor:** Operator.

**Goal:** Five dashboards, live in every environment from the start.

Happy path:

1. **Upload funnel:** started → completed → failed over time, with success rate (the <99% alert is computed from this).
2. **Link funnel:** `transfer_page_viewed` → `download_started` → `download_completed`; conversion % and `link_to_download_seconds`.
3. **Job health:** `expiry_job_lag_seconds` + deletion job lag; red when the >30 min alert trips.
4. **Email health:** `email.sent` vs `email.failed` per hour; DLQ count.
5. **Business:** transfers/day, storage active (`storage_bytes_active`), active transfers, plan mix (P1-ready).

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

## Exit check

- [ ] Scenario 1: the five dashboards exist in dev App Insights
- [ ] Scenario 2: expiry is delayed artificially (flag RETENTION_DAYS=0 + paused job)
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-012/US-012-02-dashboards.md`
- Feature: `TRF-012-telemetry.md` (FR-012-3)
- Architecture: TA-10.4, TA-10.2
- Milestone: T-025
