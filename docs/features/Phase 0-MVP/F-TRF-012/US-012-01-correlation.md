# US-012-01 — Trace a request end-to-end

**Feature:** F-TRF-012 — Telemetry & Observability | **Status:** pending

---

**Story:** As a developer debugging "my transfer's email never sent", I want to follow one correlation id from the browser through the API, the event bus, and the worker, so that I find the broken hop in minutes.
**Actor:** Developer/operator with App Insights access.
**Goal:** One id, the whole journey.

## Preconditions

- W3C `traceparent` accepted by the API (T-008 pipeline foundation).

## Happy path

1. Browser request carries (or gets) a `traceparent`; the API derives `CorrelationId` = first 8 hex of the trace-id.
2. The id is echoed in: logs, Problem+JSON errors (`correlationId` field), and the `transfer.created` Service Bus envelope (`correlationId` field, TA-5.2).
3. `f-email` consumes the event, stamps the same id onto its function instance's logs and its `email.sent` event.
4. In App Insights: search the id → API request, event emission, worker consumption, and send — one chain.

## Alternative flows

- **Timer job (no incoming trace):** `f-expire` generates its own trace and uses the job window id as the correlation basis (EC-012-1) — `transfer.expired` events are still findable by transfer id.
- **User reports an error:** they quote "Ref: a1b2c3d4" → same search.

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

## UI notes

- The only user-visible surface: "Ref: {id8}" on error screens (F-TRF-013, US-013-01).

## Technical notes

- Pipeline: correlation middleware (T-008) → `IEventPublisher` stamps the envelope (TA-5.2) → workers read it from the message header/field.
- Serilog enrichment with the correlation id on every log line (TA-10.5).

## Links

- Feature: `TRF-012-telemetry.md` (FR-012-2)
- Plan AC: AC-012-1
- Architecture: TA-10.1, TA-5.2
- Related: US-013-01 (the user-facing Ref id)
- Milestone: T-008, T-025
