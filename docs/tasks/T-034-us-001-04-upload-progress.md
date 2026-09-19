# T-034 — Watch per-file and overall progress

**Story:** US-001-04 | **Feature:** F-TRF-001 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-001/US-001-04-upload-progress.md`
**Coarse task (Milestone-Backlog.md):** T-012
**Status:** pending

---

## Scope

As a sender uploading a 4 GB video, I want to see exactly how much of each file and of the whole transfer has been uploaded, so that I never guess whether the transfer is working.

**Actor:** Any user who has pressed "Send." and is uploading staged files.

**Goal:** Turn a long, silent network operation into a visible, byte-accurate progress display.

Happy path:

1. After "Send.", each file row shows a 3 px progress bar and a percentage.
2. An overall line shows combined uploaded bytes vs total bytes ("1.2 GB of 4.0 GB").
3. Percentages are computed from **bytes uploaded**, not block count (FR-001-5).
4. When a file hits 100 % it is marked done (check icon) and the next file starts.
5. When all files are done, the overall line shows 100 % and the flow advances to the link screen (F-TRF-002).

## Acceptance criteria

```gherkin
Given I have 3 files staged and I press "Send."
When the upload runs
Then every file row shows a live percentage based on bytes uploaded
And the overall line shows combined progress
When all files reach 100%
Then "Send." advances to the link screen

Given a file row is at 100%
When the next file starts
Then the completed row shows a done check and the next row begins filling
```

## Edge cases

- The progress state is live (Zustand store), so no polling.
- A failed file pauses only its own row (US-001-05); overall % keeps counting the completed bytes.
- If the user leaves the page mid-upload, the session is lost in MVP (block-level resume is within-session only, FR-001-6).

## Exit check

- [ ] Scenario 1: I have 3 files staged and I press "Send."
- [ ] Scenario 2: a file row is at 100%
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-001/US-001-04-upload-progress.md`
- Feature: `TRF-001-upload-surface.md` (FR-001-4, FR-001-5, FR-001-7)
- Plan AC: AC-001-1
- Architecture: TA-8.3 (UploadEngine), TA-4.2#1, TA-10.2
- Design: UI-Reference §4.2, §5.1
- Milestone: T-012
