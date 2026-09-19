# T-051 — Transfers expire automatically

**Story:** US-005-01 | **Feature:** F-TRF-005 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-005/US-005-01-auto-expiry.md`
**Coarse task (Milestone-Backlog.md):** T-018
**Status:** pending

---

## Scope

As the operator, I want every transfer to expire on its own after the retention window, so that nothing lives on storage longer than its plan promises.

**Actor:** Operator (system-level); recipient sees the consequence (expired screen).

**Goal:** `Active` transfers become `Expired` within one job period after `ExpiresAtUtc`.

Happy path:

1. A transfer is sent → `ExpiresAtUtc = send time + RETENTION_DAYS` (plan value).
2. After the deadline, `f-expire` finds it via `IX_Transfer_Expiry` (`Status=1 AND ExpiresAtUtc < now`).
3. In a batch transaction: `Status = Expired`, `ExpiredAtUtc = now`; `transfer.expired` emitted per row.
4. The recipient page now shows the expired screen (F-TRF-003-8): "This transfer has expired." + sender email if provided + **Send something**; no file list, no SAS minted.
5. The sender can still re-send inside the grace window (F-TRF-010).

## Acceptance criteria

```gherkin
Given an active transfer whose expiry passed 1 hour ago
When f-expire runs
Then the transfer is marked Expired with ExpiredAtUtc set
And a transfer.expired event is emitted
And the recipient page shows the expired screen without any file list

Given the expiry job runs twice in a row
When the second run processes the same window
Then no duplicate events are emitted and no error occurs
```

## Edge cases

- UTC only; ±2 min tolerance against SQL/host clock skew (EC-005-1).
- Batches of 500; cursor-exhaustion loop; a slow DB never starves the job window.

## Exit check

- [ ] Scenario 1: an active transfer whose expiry passed 1 hour ago
- [ ] Scenario 2: the expiry job runs twice in a row
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-005/US-005-01-auto-expiry.md`
- Feature: `TRF-005-expiry-deletion.md` (FR-005-1, FR-005-2, FR-005-5, FR-005-9)
- Plan AC: AC-005-1 (first clause), AC-005-2
- Architecture: TA-6.3, TA-6.1, TA-7.4
- Milestone: T-018
