# T-012-03 — Five TA-10.4 dashboards in dev App Insights (FR-012-2, AC-012-1 block 1 / US-012-02 ACs)

**Story:** US-012-02 | **Spec:** FR-012-2 / US-012-02 AC Gherkin (both blocks); TA-10.4, TA-10.2 | **Size:** M
**Depends on:** T-012-01 + T-012-02 (real data flowing: server events with correlationId + browser upload/download events)

---

## Context to read (only these)

- `US-012-02-dashboards.md` → Happy path 1–5 + Alternative flows + Edge cases
- `../F-TRF-012-telemetry.md` → FR-012-2 + AC-012-1 (block 1: "All 5 dashboards live in dev App Insights")
- TA-10.4 + TA-10.2 in `docs/03-technical-architecture.md` → the five named dashboards and the closed event/metric list they're built from

## Instructions

1. Create **exactly the five dashboards** named per TA-10.4 — "Upload funnel", "Link funnel", "Job health", "Email health", "Business" — in **dev App Insights** (`wa-dev-ai`), as App Insights dashboard items (not product UI).
2. Tiles query **only closed-set names** (TA-10.2): events for counts (e.g. `upload_started` / `upload_completed` / `upload_failed` over time; `transfer_page_viewed` → `download_started` → `download_completed`; email sent vs failed), metrics for the numeric lines (`link_to_download_seconds`, `expiry_job_lag_seconds`, `email_failures_1h`, `active_transfers`, `storage_bytes_active`).
   - **Upload funnel** success rate is a *dashboard tile* computed from upload_started/upload_completed/upload_failed — the alert's `upload.success_rate` metric is T-012-04's concern (see its ASSUMPTION line).
   - **Job health:** expiry job lag + deletion-job lag lines, threshold marker at 30 min (TA-10.3 "Expiry lag" row) so the tile visibly approaches red as AC block 2 expects.
   - **Business:** transfers/day, `storage_bytes_active`, active transfers; plan mix is P1-ready — a placeholder tile or an explicit comment suffices at M0 (no plan data emitted yet).
3. Timeframes: 24 h / 7 d / 30 d presets; UTC base (US-012-02 Edge cases). Queries must **tolerate gaps** — a missing event renders zero, not an error tile (Alternative flows "Data gap").

## Exit check

- [ ] US-012-02 AC block 1: in dev App Insights, after one test transfer + one test download, Upload funnel and Link funnel reflect the events within 5 minutes; no tile shows an error state
- [ ] US-012-02 AC block 2: artificially delaying expiry (RETENTION_DAYS=0 flag + paused job per the Gherkin) makes the Job health lag line rise toward the 30-min threshold
- [ ] All five dashboards exist in dev, named exactly per TA-10.4; every query uses a TA-10.2 name only

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (dev App Insights resource wa-dev-ai).
Task T-012-03 — the five TA-10.4 dashboards (milestone task T-025, exit check line 1: "All 5 dashboards live in dev App Insights").
Read first (only): US-012-02-dashboards.md (Happy path 1–5 + Alternative flows + Edge cases), F-TRF-012-telemetry.md (FR-012-2), TA-10.4/TA-10.2 in docs/03-technical-architecture.md.
Do exactly:
1. Five App Insights dashboards in wa-dev-ai, named verbatim per TA-10.4: Upload funnel, Link funnel, Job health, Email health, Business.
2. Tiles only from closed-set names (TA-10.2). Job health shows expiry + deletion lag with a 30-min threshold marker. Business plan-mix is an explicit M0 placeholder (P1-ready).
3. 24h/7d/30d presets, UTC base; queries tolerate gaps (zero, not error tile).
Done when: US-012-02's AC blocks hold — funnels reflect a test transfer + download within 5 minutes, the forced expiry lag rises toward threshold on Job health, and no tile errors.
Constraints: dashboards are read-side only (they write nothing to the product); no new events/metrics — a need for one is a TA-10.2 contract change (ADR-level), not a dashboard fix.
```
