# US-003-06 — Download from a phone

**Feature:** F-TRF-003 — Recipient Download Page | **Status:** pending

---

**Story:** As a recipient whose link arrived in a text message, I want the transfer page to work beautifully on my phone, so that I can get the files right away without hunting for a computer.
**Actor:** Recipient on mobile Safari (iOS) or Chrome (Android), opening `/t/{linkId}`.
**Goal:** One-tap download on a phone: file list, password card, and all states render single-column with a sticky download button.

## Preconditions

- An active (or terminal-state) transfer link opened on a phone viewport (≥ 360 px wide).

## Happy path

1. Recipient taps the link in their phone: page renders single-column, no horizontal scroll (FR-003-10, FR-017-5).
2. Password-protected: the single password card fills the screen comfortably; keyboard + button reachable without scrolling past it.
3. Active: file list with per-file **Download** and (≥2 files) **Download all** — the button is **sticky at the bottom** of the viewport; the page scrolls under it.
4. Tapping **Download all** → the zip downloads (Safari download bar on iOS; Downloads folder on Android) with original names inside (FR-017-3).
5. Tapping a per-file **Download** → the single file downloads with its original name.

## Alternative flows

- **Flaky 4G on a large file:** Range-resume works natively against the SAS URL (EC-003-1, EC-017-2).
- **Zip canceled in iOS Safari:** per-file buttons remain the documented fallback (EC-004-4).
- **Terminal states (expired/limit/unknown):** same single-screen shapes as desktop, full-width card, sticky CTA "Send something" (US-003-05).

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

## UI notes

- Breakpoint ≤ 640 px: single column everywhere (UI-Reference §3); file rows keep their min height; inputs 44 px tall, 16 px font (no iOS zoom-on-focus).
- Sticky bottom **Download / Download all** on the recipient page (UI-Reference §5.3, FR-003-10).
- Password card: single field + full-width primary button, centered.

## Technical notes

- TTI < 1 s on 4G for `/t/{linkId}` on mobile (TA-15, TA-8.4 bundle budget applies).
- E2E: Playwright mobile context (iPhone profile) over the password flow + download-all (F-TRF-017 test plan, T-027).
- Download mechanics are server-identical to desktop — only the layout/input surface differs.

## Links

- Feature: `TRF-003-recipient-page.md` (FR-003-10)
- Related: US-017-02 (mobile download story), US-003-04 (password on mobile)
- Architecture: TA-8.4, TA-15, TA-14.1
- Design: UI-Reference §3, §5.3
- Milestone: T-014, T-027
