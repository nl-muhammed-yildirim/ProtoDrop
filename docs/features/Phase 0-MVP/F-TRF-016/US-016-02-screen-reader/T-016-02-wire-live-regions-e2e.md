# T-016-02 — Wire live regions into all P0 surfaces + injected-failure E2E

**Story:** US-016-02 | **Spec:** FR-016-3, AC-016-2, EC-016-1 | **Size:** M
**Depends on:** T-016-01 (`useAriaLive` helper — this task consumes it; no new ARIA primitives)

---

## Context to read (only these)

- `US-016-02-screen-reader.md` → happy path, Alternative flows
- `../F-TRF-016-a11y.md` → FR-016-3 + UI notes (live-region placement) + Test plan (E2E bullet)
- `T-016-01-aria-live-helper.md` → the helper's contract (mapping, throttle, mount rule)

## Instructions

1. **Upload surface:** mount the hidden `role=status` container with the upload list (F-TRF-016 UI notes — one per stateful screen, mounted with the screen, not on first use). Announce overall progress through T-016-01's throttle: 0/25/50/75/100 % only (EC-016-1). On a file failure announce "{name} failed. Press Enter to retry." — the row's Retry button is the focusable action (US-016-01); bulk failures collapse to one summary announcement (e.g. "3 files failed").
2. **Toasts:** success toasts render with `role=status` (polite), error toasts with `role=alert` (assertive) — matching their visual channel (UI-Reference §4.3). The mapping comes from the T-016-01 helper, not per-screen attributes.
3. **Password card:** mount its hidden container with the card; a wrong-password submit lands "Wrong password" in `role=alert` — the shake is visual sugar, the text is the contract (FR-016-5).
4. **Recipient page:** after each download, announce the new "Downloads left: N" value politely — the count change is never silent (US-016-02 happy path 5). The zip "Preparing your download…" state is announced exactly once, not re-announced per poll (US-016-02 Alternative flows); the growth-loop panel's appearance is announced politely as a new actionable region.
5. **Row labels:** file-row `aria-label`s are the single source of truth for the spoken name — "Remove {name}", "Download {name}, {size}" (F-TRF-016 UI notes). No second, drifting label text anywhere.
6. **E2E:** Playwright with an injected upload failure asserts the live-region contents during the run: progress announcements at the throttle points, the failed-file announcement, and the error toast's `role=alert` (F-TRF-016 Test plan).

## Exit check

- [ ] Upload progress is heard politely at 0/25/50/75/100 % — a mid-tick (e.g. 43 %) does NOT announce; a failure announces the name + Retry action (AC-016-2, EC-016-1)
- [ ] Error toast asserts `role=alert`, success toast asserts `role=status` in the DOM (FR-016-3; US-016-02 gherkin "successful toast")
- [ ] Wrong password → "Wrong password" present in a `role=alert` region after submit (US-016-02 gherkin; FR-016-5)
- [ ] Recipient page: download decrements the visible count AND announces "Downloads left: N"; zip "preparing" announced once only (US-016-02 happy path 4–5 + Alternative flows)
- [ ] Playwright injected-failure E2E green: live-region contents asserted during the run, not just final state (F-TRF-016 Test plan)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS).
Task T-016-02 — wire ARIA live regions into all P0 surfaces + injected-failure E2E (milestone task T-027, F-TRF-016 part 2).
Read first (only): docs/AGENT.md; docs/features/Phase 0-MVP/F-TRF-016/US-016-02-screen-reader/US-016-02-screen-reader.md (happy path, Alternative flows); T-016-01's task file (helper contract); F-TRF-016-a11y.md (FR-016-3 + UI notes + Test plan).
Do exactly: mount the hidden role=status container with the upload list and announce overall progress via the T-016-01 throttle (0/25/50/75/100 % only — EC-016-1); failed file → "{name} failed. Press Enter to retry." with bulk failures as one summary; toasts role=status (success) / role=alert (error) per UI-Reference §4.3 via the helper's mapping; password card gets its own container, wrong submit → "Wrong password" in role=alert (FR-016-5); recipient page announces "Downloads left: N" politely after each download and the zip "Preparing your download…" state exactly once; row aria-labels ("Remove {name}", "Download {name}, {size}") are the single source of truth for spoken names; add the Playwright E2E with an injected upload failure asserting live-region contents during the run (progress points, failure announcement, error toast role).
Done when: AC-016-2 holds end-to-end — overall progress updates announced politely at 0/25/50/75/100 % and a failed file is announced with its Retry action — plus every gherkin in US-016-02's Acceptance criteria verified (FR-016-3, EC-016-1).
Constraints: wa.web only; no third-party a11y kit (TA-8.2); live regions are pure UI state — no network calls on announce; the throttle and role mapping must come from T-016-01's helper, never re-implemented per screen. Commit "feat: T-016-02 {one line}".
```
