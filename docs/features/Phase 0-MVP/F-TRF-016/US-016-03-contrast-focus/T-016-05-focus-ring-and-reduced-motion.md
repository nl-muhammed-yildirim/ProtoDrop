# T-016-05 — Global :focus-visible ring rule + prefers-reduced-motion pass

**Story:** US-016-03 | **Spec:** FR-016-4, AC-016-3, FR-016-5 | **Size:** S
**Depends on:** T-016-03 (keyboard reachability proven first — the ring makes that focus *visible*)

---

## Context to read (only these)

- `US-016-03-contrast-focus.md` → happy path 1 + 2, Alternative flows
- `../F-TRF-016-a11y.md` → FR-016-4/5 + UI notes (focus ring line) + Technical notes

## Instructions

1. **Global ring rule:** a single `:focus-visible` CSS rule in `wa.web` (the global layer, e.g. alongside the existing focus-visible styles in `index.css`) — 2 px solid `--accent`, 2 px offset, applied to every focusable element (FR-016-4; UI-Reference §3). The ring is an **`outline`**, not a box-shadow — that's why it survives OS high-contrast/forced-colors palettes (US-016-03 Edge cases).
2. `:focus-visible`, never bare `:focus` for mouse — keyboard focus always rings; mouse clicks don't leave rings everywhere (US-016-03 Alternative flows, F-TRF-016 Technical notes).
3. **Sweep for kill rules:** find every `outline: none` / `outline: 0` in features and components; each must have a replacement (the global ring qualifies) — "never outline:none without replacement" is AC-016-3's literal contract.
4. **Reduced motion:** under `@media (prefers-reduced-motion: reduce)` disable all animation/transitions across P0 surfaces (FR-016-5, UI-Reference §3). The wrong-password shake stays *fallback-safe*: the text "Wrong password" still says everything — the shake is an addition that may vanish (US-016-03 Alternative flows; FR-016-5).
5. **Playwright assert:** computed outline on keyboard-focused elements — 2 px width, `--accent` color, 2 px offset — on at least one control per P0 surface (F-TRF-016 Test plan: "focus ring visible (screenshot/box-model assert)").

## Exit check

- [ ] Keyboard focus shows a 2 px solid `--accent` ring with 2 px offset on every focused element of the walkthrough set — drop zone button, row removes, Send., link-screen fields, Copy (AC-016-3; FR-016-4)
- [ ] No bare `:focus` ring for mouse: clicking around leaves no persistent rings (US-016-03 Alternative flows)
- [ ] Every `outline: none` in the codebase has a documented replacement — zero naked occurrences (AC-016-3)
- [ ] With `prefers-reduced-motion` emulated, no animation runs on any P0 surface; wrong-password text still reads "Wrong password" without the shake (FR-016-5)
- [ ] Playwright box-model/screenshot assert of the computed outline is green (F-TRF-016 Test plan)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS).
Task T-016-05 — global :focus-visible ring rule + prefers-reduced-motion pass (milestone task T-027, F-TRF-016 part 5).
Read first (only): docs/AGENT.md; docs/features/Phase 0-MVP/F-TRF-016/US-016-03-contrast-focus/US-016-03-contrast-focus.md (happy path 1+2, Alternative flows); F-TRF-016-a11y.md (FR-016-4/5 + UI notes focus ring line + Technical notes).
Do exactly: add ONE global :focus-visible rule in wa.web's shared CSS layer — outline 2 px solid --accent with 2 px offset on every focusable element (:focus-visible, never bare :focus for mouse; the ring is an outline so it survives OS high-contrast/forced-colors); sweep all outline:none/outline:0 occurrences and ensure each has a replacement (AC-016-3's "never outline:none without replacement"); under @media (prefers-reduced-motion: reduce) disable all animation/transitions on P0 surfaces, keeping the wrong-password shake fallback-safe (the text "Wrong password" carries it — FR-016-5); add the Playwright assert of computed outline (2 px width, --accent color, 2 px offset) for keyboard-focused elements across the P0 walkthrough set.
Done when: AC-016-3 holds — a visible focus ring appears on every focusable element when focused via keyboard, and FR-016-5's reduced-motion behavior is verified (F-TRF-016 Test plan).
Constraints: CSS in the global layer only — no per-component ring duplication; contrast values are T-016-06's scope, do not retune tokens here. Commit "feat: T-016-05 {one line}".
```
