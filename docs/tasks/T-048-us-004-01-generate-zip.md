# T-048 — Generate a zip of the whole transfer

**Story:** US-004-01 | **Feature:** F-TRF-004 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-004/US-004-01-generate-zip.md`
**Coarse task (Milestone-Backlog.md):** T-017
**Status:** pending

---

## Scope

As a recipient who wants everything at once, I want "Download all" to give me one zip with every file, so that I don't download files one by one and lose track of what I got.

**Actor:** Recipient (no account) on the page of a transfer with ≥2 files.

**Goal:** One click → one zip containing the whole transfer, with original file names.

Happy path:

1. Recipient presses **Download all**.
2. The API answers 202 (zip not ready yet) and the button becomes "Preparing your download…".
3. `f-zip` streams the files (ordered by `SortOrder`) into `transfers/{id}/all.zip` and mints a 30-min SAS.
4. The client polls every 2 s; on 200 it triggers the browser download of the zip.
5. The recipient gets a zip whose entries are the original file names, top-level, no folders.

## Acceptance criteria

```gherkin
Given a 3-file transfer that is active
When I press "Download all"
Then the button shows the preparing state
And after polling I receive a zip
And the zip contains all 3 files with their original names

Given two files named "report.pdf" and "report.pdf"
When the zip is generated
Then the zip contains "report.pdf" and "report_1.pdf"
And both file contents are intact
```

## Edge cases

- Generation streams with a 4 MB buffer — a 4 GB zip never fills the function's memory (EC-004-1).
- If the transfer expires mid-generation, the partial zip is deleted; the next click regenerates (EC-004-2).

## Exit check

- [ ] Scenario 1: a 3-file transfer that is active
- [ ] Scenario 2: two files named "report.pdf" and "report.pdf"
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-004/US-004-01-generate-zip.md`
- Feature: `TRF-004-download-zip.md` (FR-004-1, FR-004-2, FR-004-5, FR-004-6)
- Plan AC: AC-004-1
- Architecture: TA-6.6, TA-4.2#7, TA-7.2
- Design: UI-Reference §5.3
- Milestone: T-017
