# T-017-03 — 44 px touch targets + no-hover affordances across P0 surfaces (FR-017-4/5)

**Story:** US-017-03 | **Spec:** FR-017-1/4/5, AC-017-3, EC-017-3 | **Size:** M
**Depends on:** T-017-01/T-017-02 (upload + recipient mobile layouts in place — this task audits *all* P0 surfaces)

---

## Context to read (only these)

- `US-017-03-touch-ui.md` → happy path, Alternative flows, Edge cases, UI notes
- `../F-TRF-017-mobile.md` → FR-017-1/4/5, AC-017-3, EC-017-3, UI notes

## Instructions

1. **Audit every P0 surface at a phone viewport** (FR-017-1's list: landing, link, recipient incl. password/download states, auth, My Files): every button, link, form control, and row action ≥ 44 × 44 px — file-row remove (✕), **Copy**, **Retry**, recipient rows, sticky **Download / Send.** (FR-017-4). 44 px is a floor: components may be taller, never shorter.
2. **No horizontal scroll at 360 px** on any P0 surface (AC-017-3): file/recipient names ellipsize with the full value via `aria-label` / a detail surface — not hover (FR-017-5); My Files rows are full-width tap targets with discrete ≥ 44 px actions, never slivers (US-017-03 Edge cases).
3. **Sticky overlap:** reserved bottom padding so the last list item is never hidden behind the sticky action (box-model verified — T-017-04's Playwright check).
4. **No hover-dependent affordances anywhere** (FR-017-5): no tooltips as the *only* channel; everything tappable. Sweep features for `hover:` / `group-hover` reveals that carry information, not decoration.
5. **Forms:** inputs 44 px tall with 16 px font (no iOS auto-zoom on focus — FR-017-4/§3); top bar collapses to logo + avatar at ≤ 640 px; file rows keep their min height (UI-Reference §3).
6. **Zoom (EC-017-3):** text selectable/scaleable — `user-scalable` not forced off in the viewport meta; layout survives 200% zoom (WCAG 1.4.4). Landscape counts as a wider viewport: single-column rules still apply.
7. Add the **44 px lint/review check** for new components (US-017-03 Technical notes — defense in depth alongside T-017-04's computed-style assertion): a lint rule or documented review gate flagging interactive elements below 44 × 44 px.
   ASSUMPTION: US-017-03 names the "lint/review check" without pinning its mechanism (eslint-plugin, custom script, or review checklist); DECISION for this task: implement it as a small `npm run lint` companion script in wa.web that walks the component map and reports interactive elements under 44 px. Record in `Open-Decisions-and-Constants.md` Part 3.

## Exit check

- [ ] Every P0 surface at 360 px: all interactive elements ≥ 44 × 44 px (computed), including file-row remove, Copy, Retry, recipient rows, sticky buttons (FR-017-4)
- [ ] No horizontal scroll at 360 px on any P0 screen; names truncate with `aria-label`/detail fallback (AC-017-3, FR-017-5)
- [ ] Last list item never hidden behind the sticky action — reserved bottom padding verified by box model (US-017-03 Edge cases)
- [ ] No element revealed only via hover; no tooltips as the sole channel (FR-017-5)
- [ ] Form inputs 44 px / 16 px font; focus on a phone does not auto-zoom (US-017-03 gherkin 2)
- [ ] `user-scalable` not forced off; layout survives 200% zoom (EC-017-3, WCAG 1.4.4); landscape single-column holds
- [ ] 44 px lint/review check runs and passes on the current component map (US-017-03 Technical notes)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS).
Task T-017-03 — 44 px touch targets + no-hover affordances audit across all P0 surfaces (milestone task T-027, F-TRF-017 part 3).
Read first (only): docs/features/Phase 0-MVP/F-TRF-017/US-017-03-touch-ui/US-017-03-touch-ui.md; F-TRF-017-mobile.md (FR-017-1/4/5, AC-017-3, EC-017-3).
Do exactly: at a 360 px phone viewport make every interactive element on every P0 surface >= 44 x 44 px (file-row remove, Copy, Retry, recipient rows, sticky Download/Send. — FR-017-4); guarantee no horizontal scroll anywhere with name truncation via aria-label/detail fallback (AC-017-3); reserve bottom padding so the last list item is never behind the sticky action; strip hover-dependent affordances (no tooltips as the only channel — FR-017-5); keep form inputs 44 px tall / 16 px font and top bar collapsed to logo + avatar at <= 640 px; do not force user-scalable off and verify 200% zoom survives (EC-017-3, WCAG 1.4.4); add a lint companion script flagging interactive components under 44 px and record the mechanism decision in Open-Decisions-and-Constants.md Part 3.
Done when: AC-017-3 holds by computed-style inspection — no horizontal scroll at 360 px and all touch targets >= 44 px on every P0 screen, with the 44 px lint check passing (FR-017-4/5).
Constraints: wa.web only; CSS fixes via design tokens where possible (tokens.css); desktop layout above 640 px unchanged; no new packages without an ADR line. Commit "feat: T-017-03 {one line}".
```
