# T-080 — Trace a request end-to-end

**Story:** US-012-01 | **Feature:** F-TRF-012 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-012/US-012-01-correlation.md`
**Coarse task (Milestone-Backlog.md):** T-008, T-025
**Status:** pending

---

## Scope

As a developer debugging "my transfer's email never sent", I want to follow one correlation id from the browser through the API, the event bus, and the worker, so that I find the broken hop in minutes.

**Actor:** Developer/operator with App Insights access.

**Goal:** One id, the whole journey.

Happy path:

1. Browser request carries (or gets) a `traceparent`; the API derives `CorrelationId` = first 8 hex of the trace-id.
2. The id is echoed in: logs, Problem+JSON errors (`correlationId` field), and the `transfer.created` Service Bus envelope (`correlationId` field, TA-5.2).
3. `f-email` consumes the event, stamps the same id onto its function instance's logs and its `email.sent` event.
4. In App Insights: search the id → API request, event emission, worker consumption, and send — one chain.

## Acceptance criteria

```gherkin
Given a transfer is sent and the email worker runs
When I search Application Insights for the correlationId
Then I see the API request, the transfer.created event, the worker consumption, and the email.sent event
And they all share the same correlationId

Given a 500 response
When I read its correlationId field
Then it equals the first 8 hex of the request's W3C trace-id
```

## Edge cases

- Idempotency replays keep the original request's id (the replay returns the stored result; logs show both with the same id).
- `correlationId` is never PII (it's an id, not a name) — safe to log at default level.

## Exit check

- [ ] Scenario 1: a transfer is sent and the email worker runs
- [ ] Scenario 2: a 500 response
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-012/US-012-01-correlation.md`
- Feature: `TRF-012-telemetry.md` (FR-012-2)
- Plan AC: AC-012-1
- Architecture: TA-10.1, TA-5.2
- Related: US-013-01 (the user-facing Ref id)
- Milestone: T-008, T-025
