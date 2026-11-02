# T-016-01 — useAriaLive helper + hidden live-region containers in core/ui

**Story:** US-016-02 | **Spec:** FR-016-3, EC-016-1 (foundation for AC-016-2) | **Size:** S
**Depends on:** nothing new — TA-8.2 `core/ui` only; the F-TRF-001 upload engine state already exists

---

## Context to read (only these)

- `US-016-02-screen-reader.md` → happy path, Edge cases
- `../F-TRF-016-a11y.md` → FR-016-3 + UI notes (live-region placement) + Test plan (unit bullet)

## Instructions

1. Add a `useAriaLive` helper in `wa.web` `core/ui`: it owns one hidden live-region element **mounted with its screen** — the container must exist before any update is made to it (screen-reader requirement; US-016-02 Edge cases), never created on first use.
2. The helper maps message priority to ARIA attributes in exactly one place so it can't drift per screen (US-016-02 Edge cases): polite → `role=status` / `aria-live="polite"`, assertive → `role=alert`. No third-party a11y kit (TA-8.2).
3. Add the announcement throttle to the helper: progress announces at **0/25/50/75/100 %** — not every tick; endpoints inclusive, so a 99→100 jump is always announced (EC-016-1, US-016-02 Edge cases). The throttle applies to all live content driven through the helper (bulk updates → one summary announcement, e.g. "3 files failed", per US-016-02 Alternative flows).
4. Placement contract: one hidden `role=status` container per stateful screen — upload list, password card (F-TRF-016 UI notes); toasts follow UI-Reference §4.3 and do not use the helper's container.
5. Unit tests for the throttle (F-TRF-016 Test plan "Unit: progress-announcement throttle"): announce points exactly 0/25/50/75/100; 99→100 announced; two consecutive failures collapse to one summary announcement.

## Exit check

- [ ] `useAriaLive` lives in `core/ui`; polite → status, assertive → alert mapping is single-sourced (FR-016-3)
- [ ] The hidden container is mounted with the screen — exists before its first update; a render test proves it's present on mount, not on first message (US-016-02 Edge cases)
- [ ] Throttle unit tests green: only 0/25/50/75/100 announced; 99→100 always announced; bulk failures → one summary (EC-016-1, F-TRF-016 Test plan)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS).
Task T-016-01 — useAriaLive helper + hidden live-region containers (milestone task T-027, F-TRF-016 part 1).
Read first (only): docs/AGENT.md; docs/features/Phase 0-MVP/F-TRF-016/US-016-02-screen-reader/US-016-02-screen-reader.md (happy path, Edge cases); F-TRF-016-a11y.md (FR-016-3 + UI notes + Test plan).
Do exactly: add useAriaLive to wa.web core/ui — one hidden live-region element mounted with its screen (must exist before updates), single-sourced mapping polite→role=status / assertive→role=alert, announcement throttle at 0/25/50/75/100 % only (endpoints inclusive, 99→100 always announced) with bulk updates collapsed to one summary announcement; placement = one hidden container per stateful screen (upload list, password card), toasts per UI-Reference §4.3 excluded from the helper's container; unit tests for the throttle per F-TRF-016 Test plan.
Done when: EC-016-1 holds in unit tests — announce points exactly 0/25/50/75/100, 99→100 inclusive, one summary announcement for bulk failures — and the container-on-mount behavior is test-proven (FR-016-3).
Constraints: wa.web only, no .NET diff; no third-party a11y kit (TA-8.2); live regions are pure UI state — no network calls on announce (US-016-02 Technical notes). Commit "feat: T-016-01 {one line}".
```
