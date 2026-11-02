# T-016-04 — Playwright keyboard-only full guest flow E2E

**Story:** US-016-01 | **Spec:** AC-016-1, FR-016-2, EC-016-2 | **Size:** M
**Depends on:** T-016-03 (ordering/focus fixes land first — this test pins them and catches regressions)

---

## Context to read (only these)

- `US-016-01-keyboard.md` → happy path (the exact step list), Technical notes
- `../F-TRF-016-a11y.md` → Test plan (E2E bullet) + FR-016-2

## Instructions

1. Add a Playwright E2E spec that runs the **full guest flow with keyboard only** — no mouse events anywhere in the test; every activation is `keyboard.press('Enter')` or `'Space'` after `page.keyboard.press('Tab')` walks (F-TRF-016 Test plan: "keyboard-only guest flow"; US-016-01 Technical notes).
2. **Assert tab *sequences*, not just reachability**: between two known elements, assert the exact order of focused elements (`activeElement` per Tab) so an ordering regression fails — this is what catches "someone added a tabindex" (US-016-01 Technical notes: the tab-sequence test catches ordering regressions).
3. Cover AC-016-1's exact chain: **select files → remove one → Send → add recipient → Send transfer → copy link**. The native file picker is an OS surface — seed staging state for it (the a11y contract is the *button* that opens it, US-016-01 Edge cases); everything after staging is real keyboard.
4. **Remove-one step:** Tab to a staged row's remove control (aria-label "Remove {name}") → Enter/Space → row gone; assert focus did not vanish onto `<body>` (a removed-element focus bug).
5. **Copy link:** the "Copied ✓" state is visible AND announced (T-016-02's live region) — assert both channels in one test step (US-016-01 happy path 5).
6. Wire into the milestone suite per T-028's e2e arrangement; the spec must run green against local dev as part of this task's gate (staging runs are T-028's job, not this task's exit).

## Exit check

- [ ] Guest flow completes on Tab/Enter/Space only: select → remove one → Send → add recipient → Send transfer → copy link — no mouse events in the spec source (AC-016-1; FR-016-2)
- [ ] The test asserts focused-element *order* for at least the landing→link-screen handoff — a deliberately misordered control would fail it (US-016-01 Technical notes)
- [ ] After removing a staged file, focus has a sane next position (not `<body>`) (AC-016-1 "remove one")
- [ ] "Copied ✓" asserted visibly AND in the live region (US-016-01 happy path 5; T-016-02 wiring)
- [ ] Drag never required: no test step and no code path marks drag as mandatory (EC-016-2)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS).
Task T-016-04 — Playwright keyboard-only full guest flow E2E (milestone task T-027, F-TRF-016 part 4).
Read first (only): docs/AGENT.md; docs/features/Phase 0-MVP/F-TRF-016/US-016-01-keyboard/US-016-01-keyboard.md (happy path, Technical notes); F-TRF-016-a11y.md (Test plan E2E bullet + FR-016-2).
Do exactly: add a Playwright E2E spec driving the full guest flow with keyboard only — Tab/Enter/Space events exclusively, no mouse; seed staging state for the native file picker (a11y contract is the button that opens it); assert tab sequences (focused element order per Tab) between key elements rather than mere reachability so ordering regressions fail; cover AC-016-1's exact chain: select files → remove one (aria-label "Remove {name}", assert focus does not fall to <body>) → Send → add recipient → Send transfer → copy link; at the end assert "Copied ✓" is both visible and announced in the T-016-02 live region.
Done when: AC-016-1 holds under Playwright — the full guest flow completes keyboard-only with tab-order assertions green (FR-016-2, EC-016-2), per F-TRF-016's Test plan "keyboard-only guest flow".
Constraints: E2E only — no production code changes unless a real ordering bug is found (then fix in markup, never via positive tabindex); staging runs land with T-028; reuse the existing Playwright harness/conventions. Commit "feat: T-016-04 {one line}".
```
