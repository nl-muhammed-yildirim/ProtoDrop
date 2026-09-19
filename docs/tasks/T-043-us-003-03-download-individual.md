# T-043 — Download individual files

**Story:** US-003-03 | **Feature:** F-TRF-003 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-003/US-003-03-download-individual.md`
**Coarse task (Milestone-Backlog.md):** T-016
**Status:** pending

---

## Scope

As a recipient who only needs one of several files, I want to download a single file directly, so that I don't have to unpack a zip for one file.

**Actor:** Recipient (any device) on an active transfer.

**Goal:** Per-file **Download** streams the file with its original filename, via a time-boxed SAS.

Happy path:

1. Recipient clicks **Download** on one file row.
2. The server mints a 30-minute read SAS (FR-003-3) and the browser streams the blob.
3. The file arrives with its **original filename** (no `.zip` wrapper, no renamed temp name).
4. `DownloadsCount` increments (MVP: counted at SAS mint); the row's state is unchanged — the file can be re-downloaded within the transfer's life.

## Acceptance criteria

```gherkin
Given an active transfer with 5 files
When I click Download on one file
Then it downloads directly (not zipped) with its original filename
And the download URL is a time-boxed SAS (30 min)
And "Downloads left" decrements by 1 (when the transfer has a finite cap)

Given a file named "Q4 report (final) v2.pdf"
When I download it
Then the saved file keeps the exact original name, including spaces and parentheses
```

## Edge cases

- Re-downloading the same file: allowed any number of times until the transfer expires or the download cap is hit (counted per request — MVP over-count, documented, F-TRF-003 technical notes).
- 0-byte file: downloads fine, shows 0 B in the list.

## Exit check

- [ ] Scenario 1: an active transfer with 5 files
- [ ] Scenario 2: a file named "Q4 report (final) v2.pdf"
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-003/US-003-03-download-individual.md`
- Feature: `TRF-003-recipient-page.md` (FR-003-1, FR-003-3, AC-003-1)
- Related: US-005-03 (cap), US-013-02 (degraded storage)
- Architecture: TA-4.2#6, TA-7.2, TA-3.6
- Design: UI-Reference §5.3
- Milestone: T-016
