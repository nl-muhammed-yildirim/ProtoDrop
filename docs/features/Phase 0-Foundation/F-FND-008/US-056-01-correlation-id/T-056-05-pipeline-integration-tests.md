# T-056-05 — Integration tests: Problem+JSON with correlationId + exact telemetry template (T-008 exit check)

**Story:** US-056-01 | **Spec:** AC-056-1/2/4 (integration half), FR-056-6 + Test plan lines | **Size:** M
**Depends on:** T-056-02 (error pipeline), T-056-04 (telemetry reporter)

---

## Context to read (only these)

- `US-056-01-correlation-id.md` → second Gherkin block (correlationId in the error body)
- `US-056-02-problem-json.md` → first Gherkin block (error shape)
- `../../F-FND-008-pipeline-foundation.md` → Test plan + AC-056-1/2/4

## Instructions

1. Add integration tests to `wa.api.integration`:
   - **404 and 500 return Problem+JSON with correlationId** — the body's `correlationId` matches the log context value for that request (US-056-01 second Gherkin block; AC-056-2 shape)
   - an **`upload_started`-style event emitted through WaTelemetryReporter** produces Serilog output with the exact TA-10.2 template name and properties (AC-056-4)

## Exit check

- [ ] Unknown route → 404 Problem+JSON, body `correlationId` == log context value
- [ ] Forced unhandled exception → 500 Problem+JSON, no stack trace in the body
- [ ] The telemetry event's template name and properties match TA-10.2 exactly

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; correlation middleware, error pipeline, and telemetry reporter in place).
Task T-056-05 — add the T-008 exit-check integration tests.
Read first (only): docs/features/Phase 0-Foundation/F-FND-008/US-056-01-correlation-id/US-056-01-correlation-id.md (second Gherkin block), US-056-02-problem-json.md (first Gherkin block), and F-FND-008-pipeline-foundation.md (Test plan).
Do exactly:
1. Add integration tests to src/wa.api.integration:
   - unknown route → 404 Problem+JSON whose body correlationId matches the log context value for that request;
   - forced unhandled exception → 500 Problem+JSON with no stack trace in the body (AC-056-2 shape);
   - an upload_started-style event emitted through WaTelemetryReporter produces Serilog output with the exact TA-10.2 template name and properties (AC-056-4).
Done when: all three assertions pass against the real pipeline — these are the T-008 exit checks; the rate-limit live verification already happened in T-056-03.
Constraints: assert exact field sets and exact template names — not just "some JSON came back"; use a Serilog test sink or in-memory capture for the log assertions.
```
