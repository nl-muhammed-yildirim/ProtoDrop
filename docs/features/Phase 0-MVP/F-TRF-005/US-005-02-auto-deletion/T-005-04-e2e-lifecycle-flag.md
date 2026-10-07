# T-005-04 — E2E: full lifecycle via admin flag override (RETENTION_DAYS=0)

**Story:** US-005-01 + US-005-02 | **Spec:** AC-005-1, FR-005-1/3/6/7, TA-7.4 | **Size:** M
**Depends on:** T-005-01…T-005-03 (all three jobs), F-TRF-011 admin flags (flag override mechanism)

---

## Context to read (only these)

- `US-005-02-auto-deletion.md` → happy path + Alternative flows
- `../../F-TRF-005-expiry-deletion.md` → AC-005-1…005-3 + Test plan (E2E line)

## Instructions

1. Add an E2E spec that drives the **full lifecycle** using an admin flag override: set `RETENTION_DAYS=0` (and a short grace) via the admin flags endpoint, then send a transfer and watch it die twice.
2. Assert the sequence end-to-end:
   - Send → link active on `/t/{linkId}`.
   - Within one job period (≤ 15 min, accelerated in test): recipient page shows the **expired screen** (no file list, no SAS minted).
   - After grace passes: `f-delete-transfers` + `f-delete-blobs` run → My Files row gone; blobs physically deleted.
3. Verify the **download-cap path** too (AC-005-3): a transfer that hits its cap before time expiry goes straight to `DownloadLimit` and is cleaned up on schedule by the same jobs.
4. Use Azurite for blob assertions (blob actually gone, not just row deleted).

## Exit check

- [ ] Flag override drives send → expired screen ≤ 15 min → deleted (My Files row gone after grace)
- [ ] Blobs physically absent after grace (Azurite assert), rows Status=4
- [ ] DownloadLimit transfer cleans up on the same schedule

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (Playwright harness + Azurite in place; admin flags endpoint available).
Task T-005-04 — write the full-lifecycle E2E.
Read first (only): docs/features/Phase 0-MVP/F-TRF-005/US-005-02-auto-deletion/US-005-02-auto-deletion.md (happy path + Alternative flows) and F-TRF-005-expiry-deletion.md (AC-005-1…005-3).
Do exactly:
1. Add an E2E spec that drives the full lifecycle using an admin flag override: set RETENTION_DAYS=0 (and a short grace) via the admin flags endpoint, then send a transfer and watch it die twice.
2. Assert the sequence end-to-end: Send → link active on /t/{linkId}; within one job period (≤ 15 min, accelerated in test) the recipient page shows the expired screen (no file list, no SAS minted); after grace passes f-delete-transfers + f-delete-blobs run and the My Files row is gone with blobs physically deleted.
3. Verify the download-cap path too (AC-005-3): a transfer that hits its cap before time expiry goes straight to DownloadLimit and is cleaned up on schedule by the same jobs.
4. Use Azurite for blob assertions — assert the blob is actually gone, not just the row deleted.
Done when: AC-005-1 and AC-005-3 hold end-to-end — flag override drives send → expired screen ≤ 15 min → deleted after grace, blobs are physically absent, and the download-cap path cleans up on schedule.
Constraints: accelerate job timers in test (do not wait real 15-min windows); reset admin flags after the spec so other tests see default retention; reuse the T-001-09/T-003-09 harness for seeding.
```
