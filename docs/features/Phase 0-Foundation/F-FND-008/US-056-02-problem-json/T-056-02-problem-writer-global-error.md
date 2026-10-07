# T-056-02 — ProblemWriter + GlobalErrorMiddleware (closed code list)

**Story:** US-056-02 | **Spec:** FR-056-2/3, AC-056-2, EC-056-2 | **Size:** M
**Depends on:** T-056-01 (correlationId in the log context — the error body echoes it)

---

## Context to read (only these)

- `US-056-02-problem-json.md` → happy path + Gherkin blocks + Edge cases
- `../../F-FND-008-pipeline-foundation.md` → FR-056-2/3 + AC-056-2 + EC-056-2 + Technical notes (T-008b line)

## Instructions

1. Add **`ProblemWriter`** serializing the exact TA-4.1.3 field set: `type`, `title`, `status`, `code`, `details`, optional `errors[]`, `correlationId`.
2. Machine `code` values are the closed TA-4.1.3 list — at M0 only **`NOT_FOUND`, `INTERNAL`, `RATE_LIMITED`** are exercised (the full list is reserved; adding a code later is an architecture change).
3. Add **`GlobalErrorMiddleware`**: every non-business exception maps to a Problem response (typically 500 `INTERNAL`) — no stack-trace leak into responses; logs keep the detail (TA-10.5).
4. A business exception with its own code (e.g. `TRANSFER_SIZE_EXCEEDED`) maps to its TA-4.1.3 status + code — not swallowed into 500 `INTERNAL` (EC-056-2).

## Exit check

- [ ] Unknown route → 404 Problem+JSON with `code: NOT_FOUND`, correlationId present
- [ ] Unhandled exception → 500 Problem+JSON with `code: INTERNAL`; stack trace in Serilog, not in the body
- [ ] No response body contains a raw stack trace (grep audit)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; correlation middleware in place per T-056-01).
Task T-056-02 — add the error pipeline.
Read first (only): docs/features/Phase 0-Foundation/F-FND-008/US-056-02-problem-json/US-056-02-problem-json.md (happy path + Gherkin blocks + Edge cases) and F-FND-008-pipeline-foundation.md (FR-056-2/3).
Do exactly:
1. Add ProblemWriter serializing the exact TA-4.1.3 field set: type, title, status, code, details, optional errors[], correlationId.
2. Machine code values are the closed TA-4.1.3 list — at M0 only NOT_FOUND, INTERNAL, RATE_LIMITED are exercised (the full list is reserved; adding a code later is an architecture change).
3. Add GlobalErrorMiddleware: every non-business exception maps to a Problem response (typically 500 INTERNAL) — no stack-trace leak into responses; logs keep the detail (TA-10.5).
4. A business exception with its own code maps to its TA-4.1.3 status + code — not swallowed into 500 INTERNAL (EC-056-2).
Done when: AC-056-2 holds — 404/500 both return Problem+JSON with the exact field set and correlationId, no stack trace in any body, and business codes map to their own status + code.
Constraints: the closed list is TA-4.1.3's table — do not invent codes at M0; RATE_LIMITED is emitted by T-056-04 (rate limiting) through this same writer.
```
