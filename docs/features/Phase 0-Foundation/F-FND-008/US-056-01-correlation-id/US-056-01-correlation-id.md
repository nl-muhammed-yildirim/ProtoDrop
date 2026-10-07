# US-056-01 — Trace every request end-to-end with one correlation ID

**Feature:** F-FND-008 — Pipeline Foundation (correlation, errors, CORS, rate limits, telemetry) | **Status:** done (T-008a, 2026-09-04)

---

**Story:** As an operator debugging a production failure, I want every request to carry one correlation ID through logs and error responses, so that "which of the 500 requests was this?" is answered by grepping one 8-hex string.
**Actor:** Ops (App Insights), developer (local Serilog output).
**Goal:** W3C `traceparent` in → out; correlationId = first 8 hex of trace-id, present in every log line for that request and in the error JSON.

## Preconditions

- Pipeline foundation in place (T-008a).
- API running with Serilog wired (F-FND-002 / US-050-03).

## Happy path

1. A request arrives with a valid W3C `traceparent` header — it is validated and reused.
2. If absent or invalid, the pipeline generates one.
3. The 8-hex correlationId is pushed into Serilog's log context for the whole request/response pair.
4. Any error response echoes it as `correlationId` in the Problem+JSON body (US-056-02).

## Alternative flows

- **Malformed traceparent:** treated as absent — a new one is generated; the request still gets a correlationId (EC-056-1).
- **Outbound calls later (T-009+):** the same context propagates to SB publish and blob operations — decided at M0.

## Acceptance criteria

```gherkin
Given a request with no traceparent header
When it completes (success or error)
Then a W3C traceparent was generated and the 8-hex correlationId appears in logs for that request

Given a failing request
When I inspect its Problem+JSON body
Then correlationId matches the log context value for that request
```

## Edge cases

- Invalid `traceparent` (malformed, wrong version) → regenerated, never crashes the pipeline.
- The correlationId is **8 hex chars** — not a full trace-id; App Insights keeps the full W3C value separately.

## UI notes

- None at M0 (Ref ID on error screens arrives with F-TRF-013 / T-025).

## Technical notes

- `W3CTraceParser` validates incoming headers; `CorrelationContext` + `CorrelationMiddleware` push the value into Serilog's log context.
- This is TA-10.1's correlation rule made executable — F-TRF-012 builds its dashboards on these IDs.

## Links

- Feature: `F-FND-008-pipeline-foundation.md` (FR-056-1, AC-056-1)
- Architecture: TA-10.1, TA-4.1.3
- Milestone: T-008a
