# T-052 — Expired storage is deleted after grace

**Story:** US-005-02 | **Feature:** F-TRF-005 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-005/US-005-02-auto-deletion.md`
**Coarse task (Milestone-Backlog.md):** T-018
**Status:** pending

---

## Scope

As the operator, I want expired transfers to be physically deleted (rows + blobs) after the grace window, so that I pay storage only for live data — and re-send (F-TRF-010) still works inside the window.

**Actor:** Operator (system-level).

**Goal:** `Expired`/`DownloadLimit` → `Deleted` at `ExpiredAtUtc + GRACE_DAYS`; blobs vanish only when the last reference is gone.

Happy path:

1. `f-delete-transfers` picks `Status IN (2,3) AND ExpiredAtUtc + GRACE_DAYS < now` (batch 100).
2. Per transfer, in a transaction: `Status=4` (`DeletedAtUtc`), `BlobRef.RefCount` decremented per `FileItem`, `FileItem` + `EmailRecipient` rows deleted, `transfers/{id}/all.zip` deleted, `transfer.deleted` emitted (reason `expiry-grace`).
3. `BlobRef` rows that hit `RefCount=0` are flagged `PhysicallyDeletedAtUtc = now + 24 h` (buffer).
4. `f-delete-blobs` (hourly) deletes those blobs + rows, swallowing `BlobNotFound` if lifecycle already swept them (EC-005-2).
5. My Files no longer shows the row; the recipient page (any of the states) has been gone or expired long before this point.

## Acceptance criteria

```gherkin
Given an expired transfer whose grace period has passed
When f-delete-transfers and f-delete-blobs run
Then the transfer row is Deleted and its FileItem and EmailRecipient rows are gone
And a transfer.deleted event with reason expiry-grace is emitted
And its blobs are physically deleted once no other transfer references them

Given the delete jobs run twice for the same window
When the second run processes the window
Then no double RefCount decrement, no duplicate events, no error
```

## Edge cases

- Download in flight during deletion: the 30-min SAS can outlive the row — documented, acceptable (EC-005-3).
- Shared blobs (re-send): refcount decides (EC-005-4); the 24 h physical buffer protects a concurrent re-send finalize.

## Exit check

- [ ] Scenario 1: an expired transfer whose grace period has passed
- [ ] Scenario 2: the delete jobs run twice for the same window
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-005/US-005-02-auto-deletion.md`
- Feature: `TRF-005-expiry-deletion.md` (FR-005-3, FR-005-6, FR-005-7, FR-005-8)
- Plan AC: AC-005-1 (second clause), AC-005-2
- Architecture: TA-6.4, TA-6.5, TA-3.5, TA-7.4
- Related: US-010-03 (shared blobs survive via refcount)
- Milestone: T-018
