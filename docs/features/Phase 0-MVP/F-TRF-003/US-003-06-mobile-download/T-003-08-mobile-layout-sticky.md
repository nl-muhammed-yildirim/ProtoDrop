# T-003-08 — Mobile layout: single column, sticky download button (FR-003-10)

**Story:** US-003-06 | **Spec:** FR-003-10, AC-003-1/2 (mobile half), TA-8.4/TA-15 | **Size:** M
**Depends on:** T-003-02 (recipient page), T-003-03 (terminal screens), T-003-05 (password card)

---

## Context to read (only these)

- `US-003-06-mobile-download.md` → happy path + Edge cases + UI notes
- `../../F-TRF-003-recipient-page.md` → FR-003-10 + Test plan (perf line)

## Instructions

1. At **≤ 640 px** the recipient page is single-column everywhere: no horizontal scroll at 360 px; file rows keep their min height; inputs 44 px tall, 16 px font (no iOS zoom-on-focus).
2. The **Download / Download all** button is **sticky at the bottom** of the viewport on mobile — the page scrolls under it; the sticky bar never overlaps the last file row (bottom padding reserved in layout).
3. Password card: single field + full-width primary button, centered; keyboard + button reachable without scrolling past them.
4. No hover-dependent affordances anywhere on the page (mobile has no hover — F-TRF-017-5); text selectable and zoomable; layout survives 200% zoom (WCAG 1.4.4).
5. Verify TTI < 1 s on 4G for `/t/{linkId}` on mobile (TA-15) — the page already mints no SAS up front (T-003-01), so this is a bundle/layout check, not an API change.

## Exit check

- [ ] At 360 px: single column, no horizontal scroll, sticky download button visible at the bottom
- [ ] Password card reachable without scrolling past it; inputs 44 px / 16 px font
- [ ] No hover-only affordances; layout intact at 200% zoom
- [ ] Playwright mobile context (iPhone profile) passes the password flow + download-all path

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (wa.web: Vite 5 + React 18 + TS strict).
Task T-003-08 — make the recipient page mobile-first.
Read first (only): docs/features/Phase 0-MVP/F-TRF-003/US-003-06-mobile-download/US-003-06-mobile-download.md (happy path + Edge cases + UI notes) and F-TRF-003-recipient-page.md (FR-003-10).
Do exactly:
1. At <= 640 px the recipient page is single-column everywhere: no horizontal scroll at 360 px; file rows keep their min height; inputs 44 px tall, 16 px font (no iOS zoom-on-focus).
2. Make the Download / Download all button sticky at the bottom of the viewport on mobile — the page scrolls under it; the sticky bar never overlaps the last file row (bottom padding reserved in layout).
3. Keep the password card a single field + full-width primary button, centered; keyboard + button reachable without scrolling past them.
4. Remove hover-dependent affordances from the page (mobile has no hover — F-TRF-017-5); text selectable and zoomable; layout survives 200% zoom (WCAG 1.4.4).
5. Verify TTI < 1 s on 4G for /t/{linkId} on mobile (TA-15) — the page already mints no SAS up front (T-003-01), so this is a bundle/layout check, not an API change.
Done when: AC-003-1/AC-003-2 hold in a Playwright mobile context (iPhone profile) — single column at 360 px with the sticky download button, password flow reachable without scrolling past it, and no hover-only affordances.
Constraints: use design tokens only; desktop layout unchanged above 640 px; the sticky bar is recipient-page-scoped CSS — do not make it global.
```
