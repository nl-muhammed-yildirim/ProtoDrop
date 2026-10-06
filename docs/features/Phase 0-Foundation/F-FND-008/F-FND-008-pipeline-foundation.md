# F-FND-008 — Pipeline Foundation (correlation, errors, CORS, rate limits, telemetry)

**Priority:** P0 (foundation) | **Phase:** 0 — Foundation (M0)
**Spec source:** `Milestone-Backlog.md` T-008a–d, TA-4.1/TA-9.5/TA-10 | **Architecture:** TA-4.1.3, TA-4.1.6, TA-9.5, TA-10.1, TA-10.2
**Milestone tasks:** T-008a, T-008b, T-008c, T-008d

---

## Description

Before any real endpoint exists, the API pipeline must already answer four questions: "which request is this?" (W3C correlation ID), "what shape is an error?" (Problem+JSON with a closed code list), "who may call us from where?" (CORS allow-list), and "how fast can they call?" (rate limits). On top of that, the telemetry SDK exposes the TA-10.2 closed event/metric set so every later feature emits events by name instead of inventing them. This is the foundation behind F-TRF-012/013: from T-009 on, no endpoint writes its own error JSON, no request lacks a correlationId, and no 5xx leaks a stack trace.

**Actors:** developer/AI session (emits telemetry events), API clients (browser SPA, guests), ops (reads correlation IDs in App Insights).
**Value:** the pipeline is decided once at M0 — T-009's first real endpoint inherits all four guarantees without re-deciding any of them.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-056-1 | Correlation ID per TA-10.1: incoming W3C `traceparent` is validated (`W3CTraceParser`) — if absent or invalid, one is generated; the correlationId (first 8 hex of trace-id) is pushed into Serilog's log context for every request/response pair and echoed in error JSON as `correlationId`. |
| FR-056-2 | Every error response is Problem+JSON per TA-4.1.3 with the exact field set (`type`, `title`, `status`, `code`, `details`, optional `errors[]`, `correlationId`). Machine `code` values are the closed TA-4.1.3 list — no others in MVP. |
| FR-056-3 | All non-business exceptions map to a Problem response via `GlobalErrorMiddleware` (typically 500 `INTERNAL`) — no stack-trace leak into responses; logs keep the detail (TA-10.5). |
| FR-056-4 | CORS per TA-4.1.6: only configured origins (`Wa:Cors:AllowedOrigins`) are allowed — prod is `https://www.{env-domain}`; local dev allows `http://localhost:5173` (AGENTS.md §5.2). Preflight cached 1 h. |
| FR-056-5 | Rate limiting at the API layer per TA-9.5 second line: in-memory `AspNetCoreRateLimit` (frozen 5.0.0), rule `*:/api/v1/auth/*` = **5/min per IP**; breached requests return Problem+JSON `RATE_LIMITED` with `Retry-After`. Front Door WAF is the first line (TA-9.5 table). |
| FR-056-6 | Telemetry contract per TA-10.2: `TelemetryEvents.cs` encodes the closed event/metric/key set; `WaTelemetryReporter` emits Serilog events with exact template names and properties — no new packages, built on the T-008a/b Serilog context pipeline. |

## Acceptance criteria

```gherkin
AC-056-1: Every request carries a correlation ID
  Given a request with no traceparent header
  When it completes (success or error)
Then a W3C traceparent is generated and the 8-hex correlationId appears in logs for that request

AC-056-2: Errors are Problem+JSON, always
  Given any failing endpoint (404, 500, rate-limited)
  When I inspect the response body
  Then it has type, title, status, code, correlationId — code from the closed TA-4.1.3 list
  And no stack trace is present

AC-056-3: Rate limiting bites on auth bursts
  Given 6 rapid requests to /api/v1/auth/* from one IP within a minute
  When the 6th arrives
  Then it returns 429 Problem+JSON code RATE_LIMITED with Retry-After

AC-056-4: Telemetry events use the closed set
  Given an upload_started-style event emitted through WaTelemetryReporter
  When I inspect its Serilog output
  Then the template name and properties match TA-10.2 exactly
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-056-1 | Invalid `traceparent` (malformed, wrong version) | Treated as absent — a new one is generated; the request still gets a correlationId (T-008a) |
| EC-056-2 | A business exception with its own code (e.g. `TRANSFER_SIZE_EXCEEDED`) | Mapped to its TA-4.1.3 status + code — not swallowed into 500 `INTERNAL` |
| EC-056-3 | Rate limit breached on a non-auth route at M0 | Not limited yet (only `/api/v1/auth/*` has the API-layer rule; WAF covers the rest per TA-9.5) |
| EC-056-4 | A feature wants a new telemetry event name | Must add it to TA-10.2 + `TelemetryEvents.cs` (closed set, golden-rule style) — ad-hoc names fail review |

## UI notes

- None at M0 (the user-facing error screens with Ref ID are F-TRF-013 / T-025).

## Technical notes

- T-008a: `W3CTraceParser` + `CorrelationContext`/`CorrelationMiddleware`.
- T-008b: `ProblemWriter` (closed list: `NOT_FOUND`, `INTERNAL`, `RATE_LIMITED` at M0 — the full TA-4.1.3 list is reserved) + `GlobalErrorMiddleware`.
- T-008c: `AspNetCoreRateLimit` 5.0.0 frozen, in-memory store; verified live against the running API.
- T-008d: `TelemetryEvents.cs` + `WaTelemetryReporter` — no new packages (existing Serilog pipeline).
- This feature is the M0 half of F-TRF-012 (telemetry contract) and F-TRF-013 (error shape); dashboards/alerts land with T-025.

## Test plan

- Integration: 404/500 return Problem+JSON with correlationId (T-008 exit check); `upload_started`-style event emitted with exact template.
- Live verification: rate limit breached against the running API (T-008c).
- Gate: full AGENT.md §4 gate green.

## User stories

| ID | Story | File |
|---|---|---|
| US-056-01 | Trace every request end-to-end with one correlation ID | `US-056-01-correlation-id.md` |
| US-056-02 | Every error is Problem+JSON from a closed code list | `US-056-02-problem-json.md` |
| US-056-03 | Burst traffic on auth doesn't take the API down | `US-056-03-rate-limit.md` |
| US-056-04 | Emit telemetry events from a closed, named set | `US-056-04-telemetry-events.md` |
