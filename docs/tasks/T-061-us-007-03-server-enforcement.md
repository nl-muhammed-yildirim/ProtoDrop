# T-061 — Limits are enforced even if the client lies

**Story:** US-007-03 | **Feature:** F-TRF-007 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-007/US-007-03-server-enforcement.md`
**Coarse task (Milestone-Backlog.md):** T-009, T-020
**Status:** pending

---

## Scope

As the operator, I want every limit enforced server-side at the money moments, so that a bug (or a determined user) calling the API directly can't sneak past the free tier.

**Actor:** Operator (rule owner); any API caller (the "liar").

**Goal:** The client-side check is UX; the server check is the law.

Happy path:

1. `POST /transfers/draft` with `files[].sizeBytes` totaling 5.2 GB (Free) → `TRANSFER_SIZE_EXCEEDED` (Problem+JSON, TA-4.1.3).
2. `POST .../send` with 21 addresses → cap error naming `MAX_EMAILS`.
3. Finalize of a 6th GB after 5 GB already active → `STORAGE_QUOTA_EXCEEDED`.
4. Each rejection names the value that tripped it (`details` carries the numbers).

## Acceptance criteria

```gherkin
Given a raw API call creating a 5.2 GB draft on the Free plan
When the draft is created
Then the response is 422 TRANSFER_SIZE_EXCEEDED
And the details contain both numbers (5.2 GB, 5 GB limit)

Given the client reports a file as 100 MB but uploads 300 MB
When finalize runs
Then the mismatch is caught against the real blob size
And the response names the file
```

## Edge cases

- Sizes are bytes (1024-based) end-to-end; no float rounding (BIGINT).
- The server check is idempotent and cheap (metadata reads); no re-upload required.

## Exit check

- [ ] Scenario 1: a raw API call creating a 5.2 GB draft on the Free plan
- [ ] Scenario 2: the client reports a file as 100 MB but uploads 300 MB
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-007/US-007-03-server-enforcement.md`
- Feature: `TRF-007-limits.md` (FR-007-2)
- Plan AC: AC-007-1
- Architecture: TA-4.1.3, TA-3.4
- Milestone: T-009, T-020
