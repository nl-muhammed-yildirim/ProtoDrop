# T-084 — Keep using the site when storage is down

**Story:** US-013-02 | **Feature:** F-TRF-013 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-013/US-013-02-degraded-storage.md`
**Coarse task (Milestone-Backlog.md):** T-025
**Status:** pending

---

## Scope

As a user whose upload or download hit a storage outage, I want to see an honest, specific message and a retry path, so that a partially-working product still feels in control.

**Actor:** Sender uploading (F-TRF-001), or recipient downloading (F-TRF-003), while Blob Storage is degraded/unreachable.

**Goal:** blob-down is a *per-item* state, not a global crash: uploads fail with a retry hint, downloads show per-file "temporarily unavailable", and the rest of the page keeps working.

Happy path:

1. **Uploading:** a block upload fails → the existing retry ladder runs (5 retries with backoff, F-TRF-001-6); after the retries, that file's row shows "Storage temporarily unavailable — retry." with a per-file **Retry** button (FR-013-3, per-file retry path F-TRF-001-6).
2. **Downloading:** the SAS-mint request fails → that file's row shows "File temporarily unavailable." in a per-file state; the page still lists all files and other rows remain downloadable (FR-013-3).
3. When storage recovers, **Retry** works — no "all clear" ceremony needed: the next successful operation just works and the row-local state resets (EC-013-2).

## Acceptance criteria

```gherkin
  When a block upload fails
  Then the retry path is used (5 retries, backoff)
  And after retries the row shows "Storage temporarily unavailable — retry."
  And a per-file Retry button is available
  And the other files' rows are unaffected

Given a recipient page while storage is down at SAS-mint time
When a download is attempted for one file
Then that row shows "File temporarily unavailable."
And the file list still renders and other rows remain actionable
```

## Edge cases

- The degradation indicator is **row-local and self-clearing**: a successful retry clears the state; there is no global banner in MVP (EC-013-2).
- Downloads in flight are not interrupted (the 30-min SAS keeps serving) — only new mints fail (cross-ref US-005-02 EC-005-3: SAS outlives the row).
- No double-announce: the row state text is the single channel (plus an `aria-live` polite announcement, F-TRF-016-3).

## Exit check

- [ ] Scenario 1: a recipient page while storage is down at SAS-mint time
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-013/US-013-02-degraded-storage.md`
- Feature: `TRF-013-errors.md` (FR-013-3, AC-013-2)
- Related: US-001-05 (upload retry ladder), US-003-03 (single-file download), US-004-01 (zip generation)
- Architecture: TA-8.3 (UploadEngine), TA-4.2#6
- Milestone: T-025
