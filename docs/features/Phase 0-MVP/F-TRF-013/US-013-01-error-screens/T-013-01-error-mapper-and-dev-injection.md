# T-013-01 — Error mapper guarantee + dev injected-failure endpoint (FR-013-6, FR-013-2)

**Story:** US-013-01 | **Spec:** FR-013-6 / FR-013-2 + AC-013-1 + EC-013-1; TA-4.1.3 verbatim | **Size:** S
**Depends on:** T-008 (pipeline foundation) — no other T-013 task

---

## Context to read (only these)

- `US-013-01-error-screens.md` → Technical notes + Edge cases (Ref id reuse)
- `../F-TRF-013-errors.md` → FR-013-2 / FR-013-6 + AC-013-1 + Test plan ("injected failures in dev")
- TA-4.1.3 in `docs/03-technical-architecture.md` → Problem+JSON shape + closed machine-code list, verbatim

## Instructions

1. Verify the existing mapper (`src/wa.api/Pipeline`: GlobalErrorMiddleware + ProblemWriter) against: every unhandled exception → 500 Problem+JSON with machine code `INTERNAL`, and every error response carries `correlationId` = first 8 hex of the W3C trace id stamped by the pipeline (T-008). BusinessException → its closed-list code. The full list is TA-4.1.3's — "all of them, no others in MVP."
2. Add a **dev-only** injected-failure endpoint for E2E (spec Test plan: "with injected failures in dev"):
   - DECISION line: the TA-4.2 catalog lists no chaos route — add `GET /api/v1/dev/chaos?kind=internal|rate_limited`, gated to the Development environment like other dev-only routes; `internal` throws a plain exception through the pipeline (forced 500), `rate_limited` returns the real 429 Problem+JSON (code `RATE_LIMITED`). If review objects to an unlisted route, surface it as ESCALATION per AGENT.md §8 rather than adding prod surface.
3. No new error codes or limits (FR-013-6): only closed-list codes; all copy/styling lives client-side in T-013-02.

## Exit check

- [ ] AC-013-1 (API half): forced 500 → Problem+JSON with `status` 500, `code` `INTERNAL`, and the error is in Application Insights under that ref
- [ ] AC-013-1 (API half): dev chaos endpoint returns the correct 500 / 429 Problem+JSON for both `kind`s; no prod route exists
- [ ] EC-013-1: two forced 500s in the same tab carry distinct `correlationId` values (8 hex of a W3C trace id)
- [ ] FR-013-6: mapper emits exactly TA-4.1.3's closed list — no other code anywhere

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (src/wa.api).
Task T-013-01 — error mapper guarantee + dev injected-failure endpoint (milestone task T-025, scope "error screens (F-TRF-013)").
Read first (only): US-013-01-error-screens.md (Technical notes + Edge cases), F-TRF-013-errors.md (FR-013-2/6, AC-013-1, Test plan), TA-4.1.3 in docs/03-technical-architecture.md.
Do exactly:
1. Verify src/wa.api/Pipeline: unhandled → 500 + code INTERNAL + correlationId (first 8 hex of the W3C trace id); BusinessException → closed-list codes only, list verbatim TA-4.1.3.
2. Add dev-only GET /api/v1/dev/chaos?kind=internal|rate_limited (DECISION line in the task file; env-gated — no prod surface).
Done when: a forced 500 returns Problem+JSON with correlationId and is found in Application Insights under that ref, two forced 500s yield distinct refs, and the mapper's code set equals TA-4.1.3 exactly (AC-013-1, EC-013-1, FR-013-6).
Constraints: no new machine codes or limits; chaos route stays out of prod; copy/styling belongs to T-013-02.
```
