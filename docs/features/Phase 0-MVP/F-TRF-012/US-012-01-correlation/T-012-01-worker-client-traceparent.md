# T-012-01 — traceparent in → out: worker log context + browser outgoing header (FR-012-1, AC-012-1)

**Story:** US-012-01 | **Spec:** FR-012-1 / AC-012-1 (both Gherkin blocks), EC-012-1; TA-10.1 "Correlation" | **Size:** M
**Depends on:** T-056-01/T-056-05 (API half already done: `W3CTraceParser` + `CorrelationMiddleware`)

---

## Context to read (only these)

- `US-012-01-correlation.md` → happy path 4–7 + Edge cases
- `../F-TRF-012-telemetry.md` → FR-012-1 + AC-012-1
- `src/wa.api/Middleware/CorrelationMiddleware.cs` + `src/wa.domain/Correlation/` → the API half that already works (do not re-do)
- `src/wa.workers/Program.cs`, any one function (`ZipFunction.cs`) and handler (`wa.application/UseCases/Transfers/ExpireTransfers.cs`) → where `Guid.NewGuid()` currently stands in for correlation

## Instructions

1. **Workers:** every wa.workers function pushes the envelope's correlation into Serilog's log context before processing (the API half of AC-012-1 is done; this is the "every worker log line" half).
   - Convention: parse the envelope `CorrelationId` — if its hex form is 32 chars (W3C trace-id, i.e. the event was published from a request), derive the traceparent from it so worker logs join the original trace; otherwise (background jobs like f-expire/f-delete-* publish with a fresh `Guid.NewGuid()` — see `ExpireTransfers.cs`) generate a new trace-id the way `W3CTraceParser` does. Set via Serilog's log context (no `AsyncLocal` across function boundaries — set once per invocation).
   - App Insights picks up W3C from HTTP triggers automatically — do not add an SDK.
2. **Browser:** every outgoing API call from wa.web carries a W3C `traceparent` header: generate one fresh value per top-level user action (a page load's batch of requests may share it; one fetch = one header), same 4-field format `00-{32hex}-{16hex}-07`. Add a tiny helper in `src/wa.web/src/core/` used by the existing call sites (auth, transfers, profile) — no new runtime dependency.
   - ASSUMPTION line: TA-10.1 says "W3C traceparent in → out" but does not prescribe browser header generation — a client-generated root trace-id is the W3C default for edge origins; note it in the helper's summary comment.
3. **Blob SAS legs** are best-effort at M0 (the browser streams storage directly with no API in between) — state this in the helper's summary comment rather than solving it.

## Exit check

- [ ] AC-012-1 block 1: one request → response → worker round trip, every log line across API + workers carries the same correlationId (8-hex; the full traceparent is also present in the App Insights dev environment)
- [ ] A browser fetch against /api/v1 includes a valid `traceparent` header; its 8-hex prefix equals the correlationId in the server logs for that request
- [ ] EC-012-1: an expired transfer's f-expire log lines carry the correlationId of the published event (background-originated trace-id when the envelope has none) — same search finds both API-side and worker-side lines

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10 + React; Serilog everywhere).
Task T-012-01 — finish FR-012-1's "traceparent in → out" (milestone task T-025, scope line: W3C traceparent in → out on API + workers + client).
Read first (only): US-012-01-correlation.md (happy path 4–7 + Edge cases), F-TRF-012-telemetry.md (FR-012-1/AC-012-1), CorrelationMiddleware.cs + wa.domain/Correlation (existing API half).
Do exactly:
1. Workers: push the envelope-derived traceparent into Serilog log context in every wa.workers function (32-hex envelope correlationId → reuse; otherwise new trace-id, W3CTraceParser convention). No App Insights SDK — log context only + auto-detect.
2. Browser: one helper in src/wa.web/src/core/ generating a W3C traceparent per top-level action; wire the existing fetch call sites (auth, transfers, profile) to send it. Client-generated root is an ASSUMPTION noted in the comment.
3. Note blob-SAS legs as best-effort M0 in the helper comment.
Done when: AC-012-1 holds — one request → response → worker round trip shares one correlationId (8-hex) across every log line, and the browser's outgoing header is a valid W3C traceparent whose 8-hex prefix matches the server logs; EC-012-1's expiry path is found by the same search.
Constraints: do not touch the API middleware (T-056-01/05 territory); no new runtime deps; tests per TA-14 (worker log-context unit + helper vitest).
```
