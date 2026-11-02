# T-017-04 — Playwright mobile contexts + real-device spot check (mobile matrix green)

**Story:** US-017-03 (+ US-017-01/02) | **Spec:** AC-017-1/2/3, FR-017-1, EC-017-3 | **Size:** M
**Depends on:** T-017-01/T-017-02/T-017-03 (mobile UI + mechanics in place — this pins them with the E2E matrix)

---

## Context to read (only these)

- `US-017-03-touch-ui.md` → Technical notes (Playwright assertions)
- `../F-TRF-017-mobile.md` → Technical notes + Test plan
- `docs/features/Phase 0-MVP/F-TRF-016/US-016-01-keyboard/T-016-04-keyboard-full-flow-e2e.md` (repo-root relative) → the existing E2E conventions to reuse

## Instructions

1. Add **Playwright mobile contexts** — iPhone 14 Safari profile and Pixel Chrome profile (F-TRF-017 Technical notes) — to the Playwright harness, per TA-14.1's E2E layer; run them over: the guest loop (select → upload → link), password flow, download-all.
2. **AC-017-1:** select-from-Photos simulated via the file input (the native picker is an OS surface — seed it, as T-016-04 did) → 2 photos staged → upload completes with progress visible → link screen + copy works (F-TRF-017 Test plan).
3. **AC-017-2:** on the 2-file transfer, "Download all" — assert the zip download where the browser exposes it (Safari profile); per-file original-filename assertions where exposed (FR-017-3).
4. **AC-017-3:** at a 360 px viewport on every P0 screen: `document.scrollWidth <= innerWidth` (no horizontal scroll) and all touch targets ≥ 44 px via computed styles (F-TRF-017 Test plan — the same assertions T-017-03's lint check makes statically).
5. **EC-017-3:** viewport meta present, `user-scalable` not forced off; a 200% zoom step leaves the layout intact (text selectable/scaleable, WCAG 1.4.4).
6. Wire into the milestone suite per T-028's e2e arrangement — green against local dev is this task's exit; staging runs + the **real-device spot check** (Safari + Chrome) close out T-027's "mobile matrix green in staging" (F-TRF-017 Technical notes).

## Exit check

- [ ] Mobile contexts (iPhone 14 Safari, Pixel Chrome) run over guest loop, password flow, download-all — all green locally (TA-14.1 E2E layer; T-027 scope)
- [ ] AC-017-1 encoded: seed picker → 2 files staged → progress visible → link + copy works in the mobile context (F-TRF-017 Test plan)
- [ ] AC-017-2 encoded: "Download all" on a 2-file transfer asserts the zip download where the browser exposes it; filenames preserved where assertable (F-TRF-017 Test plan)
- [ ] AC-017-3 encoded: every P0 screen at 360 px → `document.scrollWidth <= innerWidth` + computed-style ≥ 44 px targets (F-TRF-017 Test plan)
- [ ] EC-017-3 encoded: no forced-off `user-scalable`; 200% zoom layout intact
- [ ] Suite wired for T-028 staging runs; real-device spot check (Safari + Chrome in staging) documented as the T-027 close-out

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS; Playwright e2e harness exists from T-009/T-016).
Task T-017-04 — Playwright mobile contexts + real-device spot check (milestone task T-027, F-TRF-017 part 4).
Read first (only): docs/features/Phase 0-MVP/F-TRF-017/US-017-03-touch-ui/US-017-03-touch-ui.md (Technical notes); F-TRF-017-mobile.md (Technical notes + Test plan); US-016-01's T-016-04 task file for the existing E2E conventions.
Do exactly: add iPhone 14 Safari and Pixel Chrome mobile contexts to the Playwright harness (TA-14.1 E2E layer) over the guest loop, password flow, and download-all; encode AC-017-1 (file-input seeded picker → 2 photos staged → progress visible → link + copy), AC-017-2 ("Download all" on a 2-file transfer asserts the zip where the browser exposes it; original filenames where assertable — FR-017-3), and AC-017-3 (every P0 screen at 360 px: document.scrollWidth <= innerWidth + computed-style >= 44 px targets); assert EC-017-3 (viewport meta present, user-scalable not forced off; 200% zoom layout intact).
Done when: the mobile matrix is green against local dev — all three ACs encoded and passing in both mobile contexts — with the suite wired into T-028's staging arrangement and the real-device spot check (Safari + Chrome) documented as T-027's close-out.
Constraints: E2E only — no production code changes unless a real bug is found (then fix in wa.web per the owning task); reuse the existing Playwright harness; seed state rather than asserting OS surfaces. Commit "feat: T-017-04 {one line}".
```
