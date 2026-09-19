# T-096 — Tap everything comfortably

**Story:** US-017-03 | **Feature:** F-TRF-017 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-017/US-017-03-touch-ui.md`
**Coarse task (Milestone-Backlog.md):** T-027
**Status:** pending

---

## Scope

As a user on a phone, I want every interactive element to be comfortably tappable and the layout to fit the screen, so that I never miss a tap or hunt for a scrolled-off button.

**Actor:** Any user on a phone viewport (mobile Safari/Chrome), on any P0 surface.

**Goal:** all touch targets ≥ 44 × 44 px (FR-017-4); no horizontal scroll at 360 px; no hover-dependent affordances; forms don't zoom-on-focus.

Happy path:

1. Every button, link, form control, and row action is ≥ 44 × 44 px (FR-017-4) — including file-row remove (✕), **Copy**, **Retry**, recipient rows, and the sticky **Download / Send.** buttons.
2. At 360 px width there is **no horizontal scroll** on any P0 screen (AC-017-3); file names truncate, sizes and actions stay reachable.
3. No hover-dependent affordances: nothing is revealed only on hover (no tooltips as the *only* channel) — everything is tappable (FR-017-5).
4. Form inputs are 44 px tall with 16 px font, so iOS doesn't auto-zoom on focus (UI-Reference §3).
5. The sticky bottom action (Download / Send.) is always reachable; content scrolls under it with reserved padding (FR-017-5).

## Acceptance criteria

```gherkin
  When inspected
  Then no horizontal scroll at 360 px width
  And all touch targets are at least 44 px

Given a form field on a phone
When it is focused
Then iOS does not auto-zoom (input is 44 px tall, 16 px font)

Given any P0 screen
When inspected
Then no element is revealed only via hover (no tooltips as the only channel)
```

## Edge cases

- **44 px vs dense lists:** My Files rows are full-width tap targets; the *action* (e.g. Re-send, Delete) is a discrete ≥ 44 px control, not a sliver (F-TRF-009 mobile layout).
- **Sticky button overlap:** the last list item is never hidden behind the sticky action (reserved bottom padding — verified in the Playwright box-model check, F-TRF-017 test plan).
- **Text truncation:** long file/recipient names ellipsize; the full value is available via the `aria-label` / a detail surface, not hover (FR-017-5).

## Exit check

- [ ] Scenario 1: a form field on a phone
- [ ] Scenario 2: any P0 screen
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-017/US-017-03-touch-ui.md`
- Feature: `TRF-017-mobile.md` (FR-017-4, FR-017-5, AC-017-3, EC-017-3)
- Related: US-017-01 (mobile upload), US-017-02 (mobile download), US-016-01 (keyboard focus on the same controls)
- Architecture: TA-8.4, TA-14.1
- Design: UI-Reference §3
- Milestone: T-027
