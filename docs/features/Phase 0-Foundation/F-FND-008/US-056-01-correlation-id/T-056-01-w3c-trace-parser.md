# T-056-01 — W3CTraceParser + CorrelationContext/CorrelationMiddleware

**Story:** US-056-01 | **Spec:** FR-056-1, AC-056-1 (first block), EC-056-1 | **Size:** M
**Depends on:** T-050-05 (Serilog wired — the correlationId lands in its log context)

---

## Context to read (only these)

- `US-056-01-correlation-id.md` → happy path + Gherkin blocks + Edge cases
- `../../F-FND-008-pipeline-foundation.md` → FR-056-1 + AC-056-1 + EC-056-1

## Instructions

1. Add **`W3CTraceParser`** validating incoming W3C `traceparent` headers — malformed or wrong-version values are treated as absent (EC-056-1), never crash the pipeline.
2. If absent/invalid, generate a new trace-id; push **correlationId = first 8 hex of trace-id** into Serilog's log context for the whole request/response pair via **`CorrelationContext` + `CorrelationMiddleware`**.
3. The full W3C value is kept separately (App Insights uses it); the correlationId is exactly 8 hex chars — not a full trace-id.

## Exit check

- [ ] A request with no traceparent gets one generated; its 8-hex correlationId appears in every log line for that request
- [ ] A valid incoming traceparent is reused (not regenerated)
- [ ] A malformed traceparent is treated as absent — new one generated, pipeline doesn't crash

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; Serilog wired per F-FND-002).
Task T-056-01 — add the correlation middleware.
Read first (only): docs/features/Phase 0-Foundation/F-FND-008/US-056-01-correlation-id/US-056-01-correlation-id.md (happy path + Gherkin blocks + Edge cases) and F-FND-008-pipeline-foundation.md (FR-056-1).
Do exactly:
1. Add W3CTraceParser validating incoming W3C traceparent headers — malformed or wrong-version values are treated as absent (EC-056-1), never crash the pipeline.
2. If absent/invalid, generate a new trace-id; push correlationId = first 8 hex of trace-id into Serilog's log context for the whole request/response pair via CorrelationContext + CorrelationMiddleware.
3. Keep the full W3C value separately (App Insights uses it); the correlationId is exactly 8 hex chars — not a full trace-id.
Done when: AC-056-1's first Gherkin block holds — no traceparent in → one generated, 8-hex correlationId present in every log line for that request; valid incoming headers are reused; malformed ones are regenerated without crashing.
Constraints: the middleware runs before everything else (correlationId must be available to error responses and telemetry); do not add App Insights SDK yet — Serilog context only at M0.
```
