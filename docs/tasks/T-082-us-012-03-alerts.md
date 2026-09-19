# T-082 — Get alerted before users complain

**Story:** US-012-03 | **Feature:** F-TRF-012 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-012/US-012-03-alerts.md`
**Coarse task (Milestone-Backlog.md):** T-025
**Status:** pending

---

## Scope

As the operator, I want alerts on the six known failure modes, so that the on-call (me, at launch) hears about it from Azure before the first "is it down?" message.

**Actor:** Operator/on-call.

**Goal:** Alert → dashboard → diagnosis, in that order, with the right severity.

Happy path:

1. A condition trips → alert fires with the metric link.
2. The alert name maps 1:1 to a dashboard tile (Job health / Email health / Upload funnel).

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

## Exit check

- [ ] Scenario 1: the six alerts are configured in dev
- [ ] Scenario 2: the DLQ is empty and no other condition is true
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-012/US-012-03-alerts.md`
- Feature: `TRF-012-telemetry.md` (FR-012-4)
- Architecture: TA-10.3
- Milestone: T-025
