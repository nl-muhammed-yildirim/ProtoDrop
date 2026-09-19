# T-046 — Download from a phone

**Story:** US-003-06 | **Feature:** F-TRF-003 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-003/US-003-06-mobile-download.md`
**Coarse task (Milestone-Backlog.md):** T-014, T-027
**Status:** pending

---

## Scope

As a recipient whose link arrived in a text message, I want the transfer page to work beautifully on my phone, so that I can get the files right away without hunting for a computer.

**Actor:** Recipient on mobile Safari (iOS) or Chrome (Android), opening `/t/{linkId}`.

**Goal:** One-tap download on a phone: file list, password card, and all states render single-column with a sticky download button.

Happy path:

1. Recipient taps the link in their phone: page renders single-column, no horizontal scroll (FR-003-10, FR-017-5).
2. Password-protected: the single password card fills the screen comfortably; keyboard + button reachable without scrolling past it.
3. Active: file list with per-file **Download** and (≥2 files) **Download all** — the button is **sticky at the bottom** of the viewport; the page scrolls under it.
4. Tapping **Download all** → the zip downloads (Safari download bar on iOS; Downloads folder on Android) with original names inside (FR-017-3).
5. Tapping a per-file **Download** → the single file downloads with its original name.

## Acceptance criteria

```gherkin
Given a recipient opens an active 2-file link in mobile Safari
When the page loads
Then the layout is single column with no horizontal scroll at 360 px
And a sticky Download button is visible at the bottom

When they tap "Download all"
Then the zip downloads (Safari download bar)
And the extracted files keep their original names

Given the same link on Android Chrome
When they tap a per-file Download
Then the single file downloads with its original filename
```

## Edge cases

- Text selectable and zoomable; layout survives 200% zoom (WCAG 1.4.4, EC-017-3).
- No hover-dependent affordances on the page (mobile has no hover — F-TRF-017-5): all state is visible without pointing.
- Sticky button never overlaps the last file row (bottom padding reserved in layout).

## Exit check

- [ ] Scenario 1: a recipient opens an active 2-file link in mobile Safari
- [ ] Scenario 2: the same link on Android Chrome
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-003/US-003-06-mobile-download.md`
- Feature: `TRF-003-recipient-page.md` (FR-003-10)
- Related: US-017-02 (mobile download story), US-003-04 (password on mobile)
- Architecture: TA-8.4, TA-15, TA-14.1
- Design: UI-Reference §3, §5.3
- Milestone: T-014, T-027
