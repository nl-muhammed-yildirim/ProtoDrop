# T-042 — Download all files in one zip

**Story:** US-003-02 | **Feature:** F-TRF-003 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-003/US-003-02-download-all.md`
**Coarse task (Milestone-Backlog.md):** T-016, T-017
**Status:** pending

---

## Scope

As a recipient of a multi-file transfer, I want to get every file in a single zip with one click, so that I'm not downloading file by file.

**Actor:** Recipient (any device) on an active transfer with ≥2 files.

**Goal:** **Download all** → one zip, original file names, top-level entries, via the same time-boxed SAS as single downloads.

Happy path:

1. Recipient clicks **Download all**.
2. If the zip is not built yet: button shows "Preparing your download…" (spinner, disabled); the UI polls every 2 s (FR-004-6).
3. When ready (HTTP 200 + URL), the browser downloads `all.zip` — a zip with one top-level entry per file, original names (deterministic `_1`/`_2` de-dup on collision, FR-004-5).
4. The download uses a 30-minute read SAS (FR-003-3); the download counts toward `DownloadsCount` (F-TRF-005).

## Acceptance criteria

```gherkin
Given an active transfer with 3 files
When I click "Download all"
Then the button shows "Preparing your download…" while generation runs
And then a zip downloads whose entries are the 3 original file names at top level

Given two staged files named "report.pdf"
When the zip is generated
Then it contains "report.pdf" and "report_1.pdf" with intact contents

Given the zip was already generated for this transfer
When I (or another recipient) click "Download all"
Then the cached zip is served instantly
And exactly one all.zip blob exists for the transfer
```

## Edge cases

- 500 MB zip on flaky 3G: browser-native Range resume against the SAS URL (EC-003-1).
- iOS Safari: the zip triggers a visible download bar; per-file buttons remain the fallback (EC-004-4).
- "Download all" consumes **one** download against `DownloadsCount` (the zip, not each file inside).

## Exit check

- [ ] Scenario 1: an active transfer with 3 files
- [ ] Scenario 2: two staged files named "report.pdf"
- [ ] Scenario 3: the zip was already generated for this transfer
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-003/US-003-02-download-all.md`
- Feature: `TRF-003-recipient-page.md` (FR-003-3, FR-003-4, AC-003-1)
- Related: US-004-01 (generation), US-004-02 (caching), US-004-03 (cap note)
- Architecture: TA-4.2#6–7, TA-6.6, TA-7.2
- Design: UI-Reference §5.3
- Milestone: T-016, T-017
