# T-041 — Open a transfer link with no account

**Story:** US-003-01 | **Feature:** F-TRF-003 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-003/US-003-01-open-link.md`
**Coarse task (Milestone-Backlog.md):** T-014
**Status:** pending

---

## Scope

As a recipient who received a transfer link, I want to open it and get my files without creating an account, so that I'm downloading within a few seconds of opening the link.

**Actor:** Recipient — no account, no cookies, any browser (desktop or phone).

**Goal:** `GET /t/{linkId}` renders the file list (names + human sizes + type icons) with **Download** per file and **Download all** when the transfer has ≥2 files — no sign-up wall.

Happy path:

1. Recipient opens `{origin}/t/{linkId}`.
2. Page renders: "From: {senderName}", the sender's note if present, and the file list (name, human-readable size, type icon).
3. Each row has **Download**; because the transfer has ≥2 files, a primary **Download all** is shown.
4. Recipient clicks **Download all** → a zip containing every file with original names starts downloading (F-TRF-004, US-004-01).
5. TTI < 1 s on 4G; no account, no cookie, no "Sign up to view" wall anywhere (FR-003-2).

## Acceptance criteria

```gherkin
  Then the file list renders with human-readable sizes in under 1000 ms (TTI on 4G)
  And the page contains "From: {senderName}"
  And every file row has a Download control
  And "Download all" is present because the transfer has more than one file

Given the recipient has no account and no stored cookies
When the page loads
Then no sign-up prompt, banner, or modal blocks the file list
```

## Edge cases

- Recipient reopens the link later in the same browser session: the page renders the same list (idempotent GET; no local state required).
- Note with long unbroken URLs: wraps/clips gracefully (UI-Reference §5.3), never breaks layout.
- The page does **not** mint SAS up front — SAS is minted only when a download is requested (TA-4.2#4), so a page view alone never consumes a download.

## Exit check

- [ ] Scenario 1: the recipient has no account and no stored cookies
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-003/US-003-01-open-link.md`
- Feature: `TRF-003-recipient-page.md` (FR-003-1, FR-003-2, AC-003-1)
- Architecture: TA-4.2#4, TA-7.2, TA-8
- Design: UI-Reference §5.3
- Milestone: T-014
