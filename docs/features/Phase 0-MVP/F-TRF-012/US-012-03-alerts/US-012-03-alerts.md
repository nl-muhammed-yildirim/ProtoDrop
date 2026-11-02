# US-012-03 — Get alerted before users complain

**Feature:** F-TRF-012 — Telemetry & Observability | **Status:** pending

---

**Story:** As the operator, I want alerts on the six known failure modes, so that the on-call (me, at launch) hears about it from Azure before the first "is it down?" message.
**Actor:** Operator/on-call.
**Goal:** Alert → dashboard → diagnosis, in that order, with the right severity.

## Preconditions

- Metrics/events per TA-10.2 flowing.

## Happy path

The six alerts (TA-10.3), each wired to App Insights → Azure Monitor → (email at MVP):

| Alert | Condition | Severity |
|---|---|---|
| Upload success low | `upload.success_rate` < 0.99 for 15 min | P1 |
| Expiry lag | `expiry_job_lag_seconds` > 1800 | P1 |
| Email failures | `email_failures_1h` > 50 | P2 |
| DLQ | `dlq_count` > 0 | P2 |
| 5xx spike | 5xx > 5% for 10 min | P1 |
| Blob 429s | storage 429 rate > 10/min | P2 |

1. A condition trips → alert fires with the metric link.
2. The alert name maps 1:1 to a dashboard tile (Job health / Email health / Upload funnel).

## Alternative flows

- **Alert fatigue guard:** no alert on `translation_missing` or PII events in MVP — only the six above.
- **Recovery:** standard Azure Monitor recovery notifications.

## Acceptance criteria

```gherkin
Given the six alerts are configured in dev
When I force a 5xx spike (test endpoint)
Then the "5xx spike" alert fires within its 10-minute window
And the alert links to the correct dashboard

Given the DLQ is empty and no other condition is true
When I wait
Then no alert fires (no false positives from test traffic in dev — dev alerts are informational)
```

## Edge cases

- Dev environment: alerts enabled but tagged informational (launch traffic is synthetic); prod severities per the table.
- Alert evaluation uses the same metric windows as the dashboards (no divergence between "what I see" and "what paged me").

## UI notes

- No product UI. Notification channel at MVP: email (Azure Monitor action group) — Teams/Slack is a P1 niceness.

## Technical notes

- Azure Monitor metric alerts on the TA-10.2 custom metrics/events; thresholds verbatim from TA-10.3.
- `dlq_count` is a custom event counter (worker emits on DLQ move; TA-10.2 includes `dlq_count`).

## Links

- Feature: `TRF-012-telemetry.md` (FR-012-4)
- Architecture: TA-10.3
- Milestone: T-025
