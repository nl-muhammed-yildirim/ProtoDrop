# T-016-03 — Keyboard access audit + focus-order fixes across P0 surfaces

**Story:** US-016-01 | **Spec:** FR-016-2, AC-016-1 (manual half), EC-016-2/3/4 | **Size:** M
**Depends on:** T-016-02 (live regions already announce errors; this task makes every announced action keyboard-reachable)

---

## Context to read (only these)

- `US-016-01-keyboard.md` → happy path, Alternative flows, Edge cases, UI notes
- `../F-TRF-016-a11y.md` → FR-016-2 + EC table + UI notes (file-row labels)
- `T-016-05-focus-ring-and-reduced-motion.md` → NOT here — ring styling + motion is T-016-05; this task only cares that the right *elements* are focusable in the right *order*

## Instructions

1. **Tab-order audit, all P0 surfaces** (FR-016-1's list: landing/upload, link screen, recipient page incl. password + download states, auth, My Files): walk each surface with Tab only; every control reachable, in DOM order — no positive `tabindex` values anywhere in features (lint/review rule, US-016-01 Edge cases). Fix ordering by moving markup, never by retabbing.
2. **Drop zone:** `role=button` + localized `aria-label` "Upload files" + the **visible Choose files button** is the canonical path — drag is additional, never required (FR-016-2, EC-016-2). The a11y contract for the native file picker lives entirely on that button: label, focus, Enter/Space activation.
3. **Focus after navigation:** landing → link screen handoff puts focus on the first actionable element (the copy field/button) — US-016-01 happy path 3. Same rule for every client-side route change in P0 flows; no "focus lost to body" states.
4. **Modals:** the `core/ui` modal component traps focus *within* the modal and restores it to the trigger on close — fix/verify there once, so My Files' confirm delete (T-009-06) and any P0 modal inherit it (US-016-01 Edge cases).
5. **Form errors (EC-016-4):** each error is linked to its field via `aria-describedby`; multi-field errors (recipient list on the link screen) additionally get a summary at the top of the form; error fields must be reachable by Tab after the error shows.
6. **Recipient page:** password unlock → both "Download all" and per-file download buttons keyboard-triggerable; the sticky mobile download bar is the *same* focusable control as its in-flow position — one element, two positions (EC-016-3). Touch-target sizing itself belongs to F-TRF-017 (≥ 44 px) — do not add padding here.
7. **File rows:** remove buttons carry `aria-label` "Remove {name}" and download buttons "Download {name}, {size}" — the labels are also T-016-02's announcement text, one source of truth (F-TRF-016 UI notes).

## Exit check

- [ ] Every P0 surface walks Tab-only in DOM order with zero skipped or doubled controls; lint/review rule "no positive tabindex in features" holds (FR-016-2, US-016-01 Edge cases)
- [ ] Drop zone is `role=button`, Enter/Space on **Choose files** opens the native picker; drag removed from any code path that *requires* it (EC-016-2)
- [ ] After landing → link screen, focus lands on the copy field/button — never `<body>` (US-016-01 happy path 3)
- [ ] Modal traps focus inside and restores to trigger on close; verified via My Files' confirm delete (US-016-01 Edge cases)
- [ ] Recipient form errors: field linked via `aria-describedby` + top-of-form summary for multi-field recipient errors (EC-016-4)
- [ ] Password link, Tab/Enter only: unlock → "Download all" AND per-file download both triggered from keyboard; sticky bar and in-flow button are one element (US-016-01 gherkin 2, EC-016-3)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS).
Task T-016-03 — keyboard access audit + focus-order fixes across all P0 surfaces (milestone task T-027, F-TRF-016 part 3).
Read first (only): docs/AGENT.md; docs/features/Phase 0-MVP/F-TRF-016/US-016-01-keyboard/US-016-01-keyboard.md (happy path, Alternative flows, Edge cases, UI notes); F-TRF-016-a11y.md (FR-016-2 + EC table + UI notes).
Do exactly: walk every P0 surface Tab-only (landing/upload, link screen, recipient page incl. password + download states, auth, My Files) and fix ordering by moving markup — no positive tabindex in features; drop zone role=button with visible Choose files button as the canonical path (EC-016-2); focus after every client-side route change lands on the first actionable element (copy field/button on the link screen); the core/ui modal traps focus inside and restores to trigger on close; form errors aria-describedby per field + top-of-form summary for multi-field recipient errors (EC-016-4); recipient page password unlock → "Download all" AND per-file downloads keyboard-triggerable, sticky mobile download bar = same focusable control as in-flow (EC-016-3); file-row labels "Remove {name}" / "Download {name}, {size}" kept as the single source of truth (T-016-02's announcement text).
Done when: AC-016-1 holds by manual walkthrough — select files → remove one → Send → add recipient → Send transfer → copy link with Tab/Enter/Space only, plus US-016-01 gherkin 2 (password unlock + both download triggers keyboard-only) verified in the browser (FR-016-2, EC-016-2/3/4).
Constraints: wa.web only; focus-order fixes move markup rather than adding tabindex values; touch-target padding is F-TRF-017's job — do not add it; ring styling belongs to T-016-05. Commit "feat: T-016-03 {one line}".
```
