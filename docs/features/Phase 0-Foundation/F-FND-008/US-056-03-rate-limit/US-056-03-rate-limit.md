# US-056-03 — Burst traffic on auth doesn't take the API down

**Feature:** F-FND-008 — Pipeline Foundation (correlation, errors, CORS, rate limits, telemetry) | **Status:** done (T-008c, 2026-09-04)

---

**Story:** As an operator defending the API against credential stuffing and runaway clients, I want auth endpoints rate-limited at the API layer with a clear machine-readable response, so that one abusive IP can't burn through the whole pipeline.
**Actor:** Ops (monitors), browser SPA (handles 429 + Retry-After).
**Goal:** `/api/v1/auth/*` limited to **5/min per IP** at the API layer; breaches return Problem+JSON `RATE_LIMITED` with `Retry-After`.

## Preconditions

- Pipeline foundation in place (T-008c).
- API running locally or in dev.

## Happy path

1. A client makes 5 requests to `/api/v1/auth/*` within one minute — all pass.
2. The 6th request in the same window is rejected: 429 Problem+JSON, `code: RATE_LIMITED`, with a `Retry-After` header.
3. After the window slides, the same IP passes again — no state beyond the in-memory store.

## Alternative flows

- **Front Door WAF (first line):** TA-9.5's per-route WAF rules (`/api/v1/auth/*` 10/min at the edge) catch most bursts before they reach the API; this story is the second, in-process line.
- **Non-auth routes at M0:** not limited at the API layer (only auth has the rule); WAF covers the rest per TA-9.5.

## Acceptance criteria

```gherkin
Given 6 rapid requests to /api/v1/auth/* from one IP within a minute
When the 6th arrives
Then it returns 429 Problem+JSON code RATE_LIMITED with Retry-After

Given the same IP after the window has passed
When it retries
Then the request passes (in-memory store, no persistent state)
```

## Edge cases

- The rule is frozen at `AspNetCoreRateLimit` **5.0.0** (package pin per TA-2.6 style — no drift).
- Breach on a non-auth route at M0 → not limited here (EC-056-3); WAF remains the guard.

## UI notes

- None at M0 (the SPA's 429 toast handling arrives with F-TRF-013 / T-025).

## Technical notes

- In-memory store — per-instance, which is correct at M0 scale; a distributed store would be an ADR change.
- Verified live against the running API (T-008c exit evidence).

## Links

- Feature: `F-FND-008-pipeline-foundation.md` (FR-056-5, AC-056-3)
- Architecture: TA-9.5, TA-4.1.3
- Milestone: T-008c
