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

## User stories & implementation tasks

| ID | Story / Task | File |
|---|---|---|
| US-016-01 | Complete a transfer without a mouse | `US-016-01-keyboard/US-016-01-keyboard.md` |
| US-016-02 | Hear what's happening (screen reader) | `US-016-02-screen-reader/US-016-02-screen-reader.md` |
| US-016-03 | See everything with clear contrast and focus | `US-016-03-contrast-focus/US-016-03-contrast-focus.md` |

**Implementation tasks:** one file per task — each story folder holds its story .md + its task files (context-friendly; execute top-to-bottom).

| Story | Task | File | Status |
|---|---|---|---|
| US-016-02 | T-016-01 `useAriaLive` helper + hidden live-region containers in `core/ui`: polite/assertive mapping, 0/25/50/75/100 % throttle (FR-016-3, EC-016-1) | `US-016-02-screen-reader/T-016-01-aria-live-helper.md` | ☐ |
| US-016-02 | T-016-02 Wire live regions into all P0 surfaces + injected-failure Playwright E2E (FR-016-3, AC-016-2) | `US-016-02-screen-reader/T-016-02-wire-live-regions-e2e.md` | ☐ |
| US-016-01 | T-016-03 Keyboard access audit + focus-order fixes across P0 surfaces (FR-016-2, EC-016-2/3/4) | `US-016-01-keyboard/T-016-03-keyboard-audit-and-focus-order.md` | ☐ |
| US-016-01 | T-016-04 Playwright keyboard-only full guest flow E2E (AC-016-1) | `US-016-01-keyboard/T-016-04-keyboard-full-flow-e2e.md` | ☐ |
| US-016-03 | T-016-05 Global `:focus-visible` ring rule + `prefers-reduced-motion` pass (FR-016-4/5, AC-016-3) | `US-016-03-contrast-focus/T-016-05-focus-ring-and-reduced-motion.md` | ☐ |
| US-016-03 | T-016-06 Contrast audit (both themes, ≥ 4.5:1) + color-independent status signals + CI check (FR-016-1/4, AC-016-4) | `US-016-03-contrast-focus/T-016-06-contrast-audit-ci-check.md` | ☐ |

**Story done when:** all tasks checked + full AGENT.md §4 gate green + the story's ACs verified (AC-016-1…AC-016-4, EC-016-1…EC-016-4). Then T-027 can be marked `done` in `Milestone-Backlog.md` — its scope is "Dark mode + a11y (WCAG 2.1 AA on P0 surfaces) + mobile web pass" and its exit check is "No flash-of-wrong-theme; keyboard-only full flow; mobile matrix green in staging" (dark-mode halves land via F-TRF-015, mobile via F-TRF-017 — this feature delivers the a11y half).
