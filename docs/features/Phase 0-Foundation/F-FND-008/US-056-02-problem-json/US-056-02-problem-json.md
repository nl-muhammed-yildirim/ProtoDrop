# US-056-02 — Every error is Problem+JSON from a closed code list

**Feature:** F-FND-008 — Pipeline Foundation (correlation, errors, CORS, rate limits, telemetry) | **Status:** done (T-008b, 2026-09-04)

---

**Story:** As the SPA developer building error handling once for all endpoints, I want every failure to arrive as Problem+JSON with a machine `code` from a closed list, so that UI branches on `code` — never on parsing free-text messages.
**Actor:** Developer (human or AI session), browser SPA (F-TRF-013 screens later).
**Goal:** 404/500/rate-limited all return the exact TA-4.1.3 shape with a `correlationId` — no stack traces, no ad-hoc codes.

## Preconditions

- Pipeline foundation in place (T-008b).
- Correlation context active (US-056-01 / T-008a).

## Happy path

1. A request hits an unknown route → 404 Problem+JSON with `code: NOT_FOUND`.
2. An unhandled exception is caught by `GlobalErrorMiddleware` → 500 Problem+JSON with `code: INTERNAL`; the stack trace stays in Serilog, not in the response.
3. Every body includes `type`, `title`, `status`, `code`, `details`, and `correlationId`.

## Alternative flows

- **Business errors later (T-009+):** codes like `TRANSFER_SIZE_EXCEEDED` map to their TA-4.1.3 status — the closed list already reserves them; at M0 only `NOT_FOUND`, `INTERNAL`, `RATE_LIMITED` are exercised.
- **Ops debugging:** grep the correlationId in logs to find the full exception detail (TA-10.5).

## Acceptance criteria

```gherkin
Given any failing endpoint at M0 (404, 500, rate-limited)
When I inspect the response body
Then it has type, title, status, code, correlationId — code from the closed TA-4.1.3 list
And no stack trace is present in the body

Given an unhandled exception in a handler
When GlobalErrorMiddleware maps it
Then the response is 500 INTERNAL Problem+JSON and Serilog holds the full detail
```

## Edge cases

- A business exception with its own code must map to its status + code — not be swallowed into 500 `INTERNAL` (EC-056-2).
- The closed list is TA-4.1.3's table: adding a code there is an architecture change, not a local decision.

## UI notes

- None at M0 — the user-facing error screens (Ref ID display) are F-TRF-013 / T-025.

## Technical notes

- `ProblemWriter` serializes the closed list; `GlobalErrorMiddleware` catches everything non-business.
- This is the M0 half of F-TRF-013: shape decided now, screens later.

## Links

- Feature: `F-FND-008-pipeline-foundation.md` (FR-056-2, FR-056-3, AC-056-2)
- Architecture: TA-4.1.3, TA-10.5
- Milestone: T-008b
