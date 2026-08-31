# F-TRF-016 — Accessibility

**Priority:** P1 | **Phase:** 0 — Core Transfer
**Spec source:** `02-feature-plan.md` F-TRF-016 | **Architecture:** TA-8.2, UI-Reference §3/§8
**Milestone tasks:** T-027

---

## Description

WCAG 2.1 AA on **all P0 surfaces** (upload, recipient page, email CTA, auth), with keyboard as a first-class input: the *entire* transfer flow works without a mouse (the drop zone has a visible **Choose files** button — F-TRF-001-1/FR-016-2). ARIA live regions announce what's happening (upload progress, toasts, password errors), focus is always visible, and color is never the only signal.

**Actors:** keyboard users, screen-reader users, low-vision users, everyone with a temporary injury (the "broken arm" test).
**Value:** accessibility is the feature that makes the product usable by ~1 in 25 of the audience — and the focus/contrast rules make it look deliberate.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-016-1 | WCAG 2.1 AA on all P0 surfaces: landing/upload, link screen, recipient page (incl. password + download states), auth, My Files. |
| FR-016-2 | **Keyboard:** the full flow (select → stage → send → link → send transfer; recipient: unlock → download) is doable without a mouse. The drop zone is `role=button` with a visible **Choose files** button (F-TRF-001). |
| FR-016-3 | **ARIA live regions:** upload progress (overall, polite — not every percent tick), toasts (`role=status`/`alert`), password errors (`role=alert`), "Downloads left" changes (polite). |
| FR-016-4 | **Focus & contrast:** visible 2 px focus ring (`--accent`, offset 2 px) on every focusable element; text contrast ≥ 4.5:1; color never the only signal (status chips carry text — UI-Reference §8). |
| FR-016-5 | **Motion:** `prefers-reduced-motion` disables all animation (UI-Reference §3); the wrong-password shake is a fallback-safe effect (it still says "Wrong password" in text). |

## Acceptance criteria

```gherkin
AC-016-1: A keyboard-only user
  When they use Tab/Enter/Space only
  Then they can complete: select files → remove one → Send → add recipient → Send transfer → copy link

AC-016-2: A screen-reader user
  When they reach the upload
  Then they hear the overall progress updates (polite live region)
  And a failed file is announced with its Retry action

AC-016-3: Any focusable element
  When focused
  Then a visible focus ring appears (never outline:none without replacement)

AC-016-4: A color-blind user (protanopia simulation)
  When they view status chips and progress
  Then status is distinguishable without color (text/labels/icons present)
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-016-1 | Progress announcements | Throttled: announce at 0/25/50/75/100% + failures — not every byte (noise) |
| EC-016-2 | Drop zone drag for keyboard users | Drag is *additional*, never required (the button is the canonical path) |
| EC-016-3 | Mobile + a11y | Touch targets ≥ 44 px (F-TRF-017-4); the recipient page's sticky download button is the same focusable control (one element, two positions) |
| EC-016-4 | Form errors | Each error linked to its field (`aria-describedby`), summary at the top of the form for multi-field errors (recipients) |

## UI notes (UI-Reference §3, §8)

- Focus ring: 2 px solid `--accent`, offset 2 px, on every focusable element (§3).
- Live region placement: one hidden `role=status` container per stateful screen (upload list, password card); toasts per §4.3.
- File rows: `aria-label` on remove buttons ("Remove {name}"), download buttons ("Download {name}, {size}").
- Contrast check per token pair (light *and* dark) against the §1 table — `--fg-faint` is the weakest token: only for placeholders/disabled.

## Technical notes

- React: `useAriaLive` helper in `core/ui`; live-region updates via `aria-live="polite|assertive"` attributes (no third-party a11y kit).
- Focus-visible CSS: `:focus-visible` (not `:focus` for mouse), ring per §3.
- E2E: Playwright `tab`-sequence assertions for the full flow (T-027: "keyboard-only full flow").

## Test plan

- E2E (Playwright): keyboard-only guest flow (AC-016-1); focus ring visible (screenshot/box-model assert); live region contents during an upload with an injected failure.
- Manual audit (T-027 exit): WCAG 2.1 AA checklist per P0 surface (contrast computed from tokens, not eyeballed).
- Unit: progress-announcement throttle (announce points 0/25/50/75/100).

## User stories

| ID | Story | File |
|---|---|---|
| US-016-01 | Complete a transfer without a mouse | `US-016-01-keyboard.md` |
| US-016-02 | Hear what's happening (screen reader) | `US-016-02-screen-reader.md` |
| US-016-03 | See everything with clear contrast and focus | `US-016-03-contrast-focus.md` |
