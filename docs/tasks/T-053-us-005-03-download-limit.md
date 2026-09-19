# T-053 — Download cap ends the transfer early

**Story:** US-005-03 | **Feature:** F-TRF-005 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-005/US-005-03-download-limit.md`
**Coarse task (Milestone-Backlog.md):** T-016, T-018
**Status:** pending

---

## Scope

As a free user, I want my transfer to stop being downloadable once its download quota is used up, so that a shared link doesn't serve files forever for free.

**Actor:** Operator (quota policy), recipient (sees the "all downloads used" screen).

**Goal:** `DownloadsCount >= MaxDownloads` → transfer ends early (`DownloadLimit`) and cleans up on schedule.

Happy path:

1. Recipients download; each mint increments `DownloadsCount`.
2. On the download-URL endpoint, when the count reaches `MaxDownloads`: set `Status = DownloadLimit` (idempotent flip, TA-7.2 step 4).
3. Subsequent page views show: "All downloads have been used." + sender email (if provided) + **Send something** — no file list, no new SAS.
4. The deletion jobs clean it up: `ExpiredAtUtc` for `DownloadLimit` transfers is set when the status flips (so the grace clock starts), or the deletion scan picks it up — same end state as expiry (TA-6.4 scans both).

## Acceptance criteria

```gherkin
Given a free transfer with MaxDownloads 100 that has 100 downloads recorded
When a page view or download request arrives
Then the transfer is marked DownloadLimit
And the page shows "All downloads have been used." with the sender email
And no new SAS is minted

Given a DownloadLimit transfer
When the deletion jobs run past its grace
Then it is deleted exactly like an expired transfer
```

## Edge cases

- The status flip and the count increment happen in the same request path (endpoint 6/7) — no job is responsible for this specific transition (TA-6.3 note 5).
- My Files chip: `DownloadLimit` renders with the expired-style chip + "Download limit reached" tooltip text (F-TRF-009).

## Exit check

- [ ] Scenario 1: a free transfer with MaxDownloads 100 that has 100 downloads recorded
- [ ] Scenario 2: a DownloadLimit transfer
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-005/US-005-03-download-limit.md`
- Feature: `TRF-005-expiry-deletion.md` (FR-005-4)
- Plan AC: AC-005-3
- Architecture: TA-7.2, TA-6.3 note, TA-4.1.3
- Related: US-003-05 (what the recipient sees)
- Milestone: T-016, T-018
