# T-060 — Know when my account storage is full

**Story:** US-007-02 | **Feature:** F-TRF-007 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-007/US-007-02-storage-quota.md`
**Coarse task (Milestone-Backlog.md):** T-020
**Status:** pending

---

## Scope

As a free user with a 5 GB account storage quota, I want to find out my storage is full at send time, so that I'm not uploading files into a full bucket.

**Actor:** Account user whose active transfers approach `STORAGE_QUOTA_FREE` (5 GB).

**Goal:** A clear "Storage full" answer with the numbers, before or at the moment of the violation.

Happy path:

1. `SUM(TotalBytes) WHERE OwnerAppUserId=@me AND Status IN (1,3)` < 5 GB → finalize proceeds.
2. When a new finalize would push the sum over 5 GB → `STORAGE_QUOTA_EXCEEDED` ("Storage full — 5 GB of 5 GB in use.").
3. The screen names what to do: delete old transfers (My Files) — P1 adds the upgrade CTA.

## Acceptance criteria

```gherkin
Given my active transfers total 4.9 GB (quota 5 GB)
When I finalize a 200 MB transfer
Then it is rejected with "Storage full"
And the message shows 5 GB of 5 GB in use

Given my active transfers total 4 GB
When I finalize a 900 MB transfer
Then finalize succeeds
And my next 1 GB send fails with Storage full
```

## Edge cases

- Only **active** states count: `Active` + `DownloadLimit` (expired-but-not-yet-deleted rows are being cleaned by jobs; the 24 h physical buffer keeps the meter honest).
- Re-send (F-TRF-010) does **not** double the meter: shared bytes counted once per `BlobRef` (see US-010-03).

## Exit check

- [ ] Scenario 1: my active transfers total 4.9 GB (quota 5 GB)
- [ ] Scenario 2: my active transfers total 4 GB
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-007/US-007-02-storage-quota.md`
- Feature: `TRF-007-limits.md` (FR-007-4)
- Plan AC: AC-007-2
- Related: US-010-03 (no double-count on re-send)
- Architecture: TA-3.4, TA-4.1.3
- Milestone: T-020
