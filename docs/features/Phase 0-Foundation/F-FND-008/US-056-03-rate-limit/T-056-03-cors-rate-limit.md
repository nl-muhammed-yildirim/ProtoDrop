# T-056-03 — CORS allow-list + auth rate limiting (5/min per IP)

**Story:** US-056-03 | **Spec:** FR-056-4/5, AC-056-3, EC-056-3 | **Size:** M
**Depends on:** T-056-02 (ProblemWriter — the 429 body uses it)

---

## Context to read (only these)

- `US-056-03-rate-limit.md` → happy path + Gherkin blocks + Edge cases
- `../../F-FND-008-pipeline-foundation.md` → FR-056-4/5 + AC-056-3 + EC-056-3 + Technical notes (T-008c line)

## Instructions

1. **CORS per TA-4.1.6:** only configured origins (`Wa:Cors:AllowedOrigins`) are allowed — prod is `https://www.{env-domain}`; local dev allows `http://localhost:5173` (AGENTS.md §5.2). Preflight cached 1 h.
2. **Rate limiting per TA-9.5 second line:** in-memory `AspNetCoreRateLimit` (**frozen 5.0.0**), rule `*:/api/v1/auth/*` = **5/min per IP**; breached requests return Problem+JSON `RATE_LIMITED` with `Retry-After`.
3. Non-auth routes are not limited at the API layer at M0 (EC-056-3) — Front Door WAF is the first line for those.

## Exit check

- [ ] 6 rapid requests to `/api/v1/auth/*` from one IP within a minute → the 6th returns 429 Problem+JSON `RATE_LIMITED` with `Retry-After` (verified **live against the running API** — T-008c exit evidence)
- [ ] After the window slides, the same IP passes again (in-memory store, no persistent state)
- [ ] Local dev origin `http://localhost:5173` gets a valid CORS response; an unlisted origin does not

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; ProblemWriter + GlobalErrorMiddleware in place).
Task T-056-03 — add the pipeline guard rails.
Read first (only): docs/features/Phase 0-Foundation/F-FND-008/US-056-03-rate-limit/US-056-03-rate-limit.md (happy path + Gherkin blocks + Edge cases) and F-FND-008-pipeline-foundation.md (FR-056-4/5).
Do exactly:
1. Add CORS per TA-4.1.6: only configured origins from Wa:Cors:AllowedOrigins are allowed — prod is https://www.{env-domain}, local dev allows http://localhost:5173 (AGENTS.md §5.2); preflight cached 1 h.
2. Add rate limiting per TA-9.5 second line using AspNetCoreRateLimit frozen at 5.0.0 with an in-memory store; rule */api/v1/auth/* = 5/min per IP; breached requests return Problem+JSON RATE_LIMITED with a Retry-After header via the existing ProblemWriter.
3. Keep non-auth routes unlimited at the API layer (EC-056-3) — Front Door WAF is the first line for those.
Done when: AC-056-3 holds live against the running API — 6 rapid auth requests from one IP yield a 429 RATE_LIMITED with Retry-After on the 6th, the window slides back to green, and CORS answers correctly for configured vs unlisted origins.
Constraints: package pin AspNetCoreRateLimit 5.0.0 exactly (no drift); in-memory store is correct at M0 scale — a distributed store would be an ADR change.
```
