# T-016-06 — Contrast audit (both themes) + color-independent status signals + CI check

**Story:** US-016-03 | **Spec:** FR-016-1, AC-016-4, EC-016-3 (touch-target cross-ref only) | **Size:** M
**Depends on:** T-015-01/US-015-02 theme parity (token pairs exist in both themes — the audit consumes them); T-016-05's ring is out of scope here

---

## Context to read (only these)

- `US-016-03-contrast-focus.md` → happy path 2–4, Edge cases
- `../F-TRF-016-a11y.md` → FR-016-1/4 + UI notes (contrast check line) + Test plan

## Instructions

1. **Contrast from tokens:** for every token pair used as text on its background in either theme, compute the WCAG contrast ratio — light *and* dark; body/label text must be ≥ 4.5:1 (FR-016-4). Computed from `tokens.css` values, not eyeballed (F-TRF-016 Test plan; US-016-03 happy path 2).
2. **CI script:** a contrast check per token pair that runs in CI — a new token pair entering `tokens.css` must pass before merge (US-016-03 Edge cases). Fail the build on any < 4.5:1 text pair; emit the ratio table as output so the audit is re-runnable, not one-shot.
3. **Weakest-token discipline:** `--fg-faint` is only for placeholders/disabled — audit every usage site and move violations to a stronger token (F-TRF-016 UI notes; US-016-03 happy path 4). This is the same rule T-015-03 established for theming — here it's asserted as an a11y gate.
4. **Color-independent status:** every status chip carries text (not just color), failed rows pair `--danger` text with the **Retry** button, the progress bar has its % / byte figure adjacent, and "Copied ✓" keeps checkmark *and* text (FR-016-4; UI-Reference §8 — F-TRF-016's Description: "color is never the only signal").
5. **Manual audit:** WCAG 2.1 AA checklist per P0 surface (landing/upload, link screen, recipient page incl. password + download states, auth, My Files) with protanopia simulation on every status chip and progress element (AC-016-4; F-TRF-016 Test plan "Manual audit").

## Exit check

- [ ] CI contrast script green: ≥ 4.5:1 for all text token pairs in both themes; a deliberately bad pair fails CI (AC-016-4 gherkin 3; FR-016-4)
- [ ] Zero `--fg-faint` usage outside placeholders/disabled — violations fixed or moved (F-TRF-016 UI notes)
- [ ] Every status chip readable under protanopia simulation — text/labels/icons present, color never the only signal; progress shows %/bytes next to the bar (AC-016-4 gherkin 2)
- [ ] Manual audit checklist complete per P0 surface in both themes — AC-016-4 gherkin + FR-016-1 surfaces all covered

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS).
Task T-016-06 — contrast audit (both themes) + color-independent status signals + CI check (milestone task T-027, F-TRF-016 part 6).
Read first (only): docs/AGENT.md; docs/features/Phase 0-MVP/F-TRF-016/US-016-03-contrast-focus/US-016-03-contrast-focus.md (happy path 2–4, Edge cases); F-TRF-016-a11y.md (FR-016-1/4 + UI notes contrast line + Test plan).
Do exactly: compute WCAG contrast ratios from tokens.css values for every text-on-background token pair in light and dark — body/label text must hit 4.5:1 (AC-016-4 gherkin 3, FR-016-4); add the CI script asserting those ratios so a new bad token pair fails before merge (US-016-03 Edge cases) with the ratio table emitted; audit --fg-faint usage sites — placeholders/disabled only, violations moved to stronger tokens; verify color-independent status: every chip carries text, failed rows pair --danger text with Retry, progress shows %/bytes adjacent, "Copied ✓" keeps checkmark and text (FR-016-4, UI-Reference §8); run the manual WCAG 2.1 AA audit per P0 surface in both themes under protanopia simulation on every status chip and progress element (AC-016-4 gherkin 2).
Done when: AC-016-4 holds — text contrast at least 4.5:1 from the token pair in both themes with --fg-faint only for placeholders/disabled, and status is distinguishable without color under protanopia simulation (FR-016-4), with the CI gate maintaining it (F-TRF-016 Test plan).
Constraints: audit consumes tokens — a token *value* change needs its own DECISION (the §1 table owns values; US-016-03 UI notes); focus ring is T-016-05's scope. Commit "feat: T-016-06 {one line}".
```
