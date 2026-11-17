# T-012-04 — Six TA-10.3 alerts wired in dev (FR-012-4, US-012-03 ACs)

**Story:** US-012-03 | **Spec:** FR-012-4 / US-012-03 AC Gherkin (both blocks) + Edge cases; TA-10.3 verbatim thresholds | **Size:** M
**Depends on:** T-012-03 (alerts link to their dashboard tiles — the dashboards must exist first)

---

## Context to read (only these)

- `US-012-03-alerts.md` → Happy path table + Alternative flows + Edge cases
- `../F-TRF-012-telemetry.md` → FR-012-4 + AC-012-1 (block 3: "All 6 alerts in dev")
- TA-10.3 in `docs/03-technical-architecture.md` → the six thresholds, verbatim

## Instructions

1. Configure **exactly the six alerts** from TA-10.3 / US-012-03's table — same names, same conditions, same severities (P1: Upload success low, Expiry lag, 5xx spike; P2: Email failures, DLQ, Blob 429s) — in Azure Monitor against **dev** resources (`wa-dev-ai` + the dev blob account). Notification channel at MVP: **email action group** (Teams/Slack is P1 — US-012-03 UI notes).
2. Each alert's action carries a link to its dashboard tile — Job health / Email health / Upload funnel (US-012-03 happy path 2); the remaining three follow the same pattern (Business for nothing; DLQ → Job health, Blob 429s → Business or none noted in the runbook).
   - `dlq_count` is already emitted as a custom event counter on DLQ move (T-056-04 reporter) — alert on the event count > 0.
   - `expiry_job_lag_seconds`, `email_failures_1h` are emitted through the T-056-04 reporter pipeline already; 5xx via App Insights' built-in request metrics.
3. **Upload success low** is the one alert whose input isn't emitted as a metric yet:
   - DECISION line: TA-10.2 lists `upload.success_rate` (custom metric) but no code emits it at M0 — configure this alert as an Azure Monitor custom query over the closed events (`upload_failed` vs `upload_started`, 5-min window, rate < 0.99 sustained 15 min) rather than adding a code-side sliding-window metric; if the query proves unworkable in dev, escalate per AGENT.md §8 instead of inventing a new emitter convention.
   - **Blob 429s:** alert on the dev blob account's standard Azure Monitor failed-requests-by-status metric (429) — no code change; note the chosen resource in the runbook comment.
4. Dev tagging: alerts enabled but tagged informational (launch traffic is synthetic); prod severities stay verbatim per the table (Edge case). Evaluation windows must equal the dashboards' windows (no divergence between "what I see" and "what paged me").

## Exit check

- [ ] US-012-03 AC block 1: forced 5xx spike via the test endpoint fires "5xx spike" within its 10-minute window in dev, alert email links to the correct dashboard tile
- [ ] US-012-03 AC block 2: with the DLQ empty and no condition true, no alert fires on ordinary dev test traffic (no false positives)
- [ ] All six alerts exist in dev with TA-10.3 names/conditions/severities verbatim; each has a dashboard link; dev-tag informational is set

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (Azure resources: wa-dev-ai App Insights + dev blob account).
Task T-012-04 — the six TA-10.3 alerts (milestone task T-025, exit check line 3: "All 6 alerts in dev").
Read first (only): US-012-03-alerts.md (Happy path table + Alternative flows + Edge cases), F-TRF-012-telemetry.md (FR-012-4/AC-012-1 block 3), TA-10.3 in docs/03-technical-architecture.md.
Do exactly:
1. Six Azure Monitor alerts against dev, thresholds verbatim from TA-10.3 (Upload success low P1, Expiry lag P1, 5xx spike P1, Email failures P2, DLQ P2, Blob 429s P2); email action group at MVP.
2. Every alert links to its dashboard tile (T-012-03 names). dlq_count/expiry_job_lag_seconds/email_failures_1h come from the existing T-056-04 reporter pipeline — no new emitters; Blob 429s via the blob account's standard failed-requests metric.
3. Upload success low as a custom query over upload_started/upload_failed (DECISION line per US spec — do not add a code-side metric without escalating).
Done when: US-012-03's AC blocks hold in dev — forced 5xx spike fires "5xx spike" within 10 min with the correct dashboard link, and ordinary dev traffic produces no false positives.
Constraints: exactly six alerts (alert fatigue guard — no translation_missing/PII alerts); dev tagged informational; evaluation windows match TA-10.3 verbatim.
```
