# T-075 — Not pay for duplicated storage

**Story:** US-010-03 | **Feature:** F-TRF-010 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-010/US-010-03-shared-blobs.md`
**Coarse task (Milestone-Backlog.md):** T-023
**Status:** pending

---

## Scope

As the operator (and the user's silent ally), I want re-sends to share the same stored bytes, so that a re-send costs nothing and the storage meter never double-counts.

**Actor:** Operator (cost owner); user (feels it as "instant re-send, no re-upload").

**Goal:** `BlobRef` reference counting: one physical blob, N transfer references.

Happy path:

1. Original transfer: `BlobRef.RefCount = 1`.
2. Re-send: new `FileItem` rows point at the **same** `BlobRefId`s; `RefCount` → 2.
3. Storage: `SUM(BlobRef.SizeBytes)` unchanged — the account meter (F-007-4) and the admin storage counter both count bytes **once**.
4. Original deleted: `RefCount` → 1 (blobs survive).
5. Re-send deleted: `RefCount` → 0 → `PhysicallyDeletedAtUtc` buffer set → blobs die (F-TRF-005, US-005-02).

## Acceptance criteria

```gherkin
Given a transfer with a 2 GB file (RefCount 1)
When I re-send it
Then no new 2 GB blob exists
And the BlobRef RefCount is 2
And the storage meter still shows 2 GB, not 4 GB

Given I delete the original and keep the re-send
When the deletion runs
Then the RefCount is 1 and the blob survives
When I delete the re-send too
Then the RefCount is 0 and the blob is physically deleted after the buffer
```

## Edge cases

- Meter integrity: storage quota (F-TRF-007-4) sums per `BlobRef`, not per `FileItem` — a 2 GB transfer re-sent 10 times still counts as 2 GB.
- The 24 h physical buffer (TA-6.4/6.5) protects against a re-send finalizing against a blob the deletion job just saw at 0.

## Exit check

- [ ] Scenario 1: a transfer with a 2 GB file (RefCount 1)
- [ ] Scenario 2: I delete the original and keep the re-send
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-010/US-010-03-shared-blobs.md`
- Feature: `TRF-010-resend.md` (the whole feature is this story's enabler)
- Plan AC: AC-010-1 (blob-sharing clause)
- Related: US-005-02 (deletion honors refcount), US-007-02 (quota math)
- Architecture: TA-3.2, TA-6.4/6.5, ADR-009
- Milestone: T-023
