# T-073 — Re-send an expired transfer in one click

**Story:** US-010-01 | **Feature:** F-TRF-010 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-010/US-010-01-resend-click.md`
**Coarse task (Milestone-Backlog.md):** T-023
**Status:** pending

---

## Scope

As a user whose link expired, I want to re-send the same transfer in one click, so that a new working link exists without re-uploading 4 GB.

**Actor:** Signed-in owner of an expired (within grace) transfer.

**Goal:** New link, fresh expiry, fresh download count — same files, zero data copy.

Happy path:

1. User presses **Re-send** (My Files, US-009-03) → pre-filled link screen.
2. `ResendTransferCommand`: new `Transfer (Status=0)`, new `FileItem` rows → **same `BlobRefId`s** with `RefCount++` each; original emails/password/note pre-filled.
3. User presses **Send transfer** → new link minted, `ExpiresAtUtc = now + RETENTION_DAYS`, `DownloadsCount=0`, `transfer.created` emitted.
4. Original transfer: `SupersededBy = newTransferId`; still downloadable if it was active.
5. Storage: **no extra bytes** (the meter is unchanged).

## Acceptance criteria

```gherkin
Given an expired transfer whose grace window is still open
When I re-send it
Then a new transfer exists with a new linkId
And it shares the original's BlobRefs (RefCount incremented, no new blob bytes)
And the original is still intact

Given the transfer's blobs were physically deleted
When I re-send it
Then I see "Files were deleted — upload again."
And no half-created transfer remains (the draft is cleaned up)
```

## Edge cases

- New linkId = fresh 8-char Crockford (F-TRF-002) — no link reuse across re-sends.
- The re-send draft pre-fill is **editable** before send (US-010-02).
- Concurrency: two re-send clicks → idempotency key (TA-4.1.5) returns the same new draft.

## Exit check

- [ ] Scenario 1: an expired transfer whose grace window is still open
- [ ] Scenario 2: the transfer's blobs were physically deleted
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-010/US-010-01-resend-click.md`
- Feature: `TRF-010-resend.md` (FR-010-1, FR-010-3, FR-010-4)
- Plan AC: AC-010-1, AC-010-2
- Architecture: TA-7.3, ADR-009
- Related: US-009-03 (entry point)
- Milestone: T-023
