# T-015-03 — Full-screen theme parity audit + token-diff CI check

**Story:** US-015-02 | **Spec:** FR-015-2, AC-015-4, EC-015-3 | **Size:** M
**Depends on:** T-015-01 (tokens + bootstrap), T-015-02 (the select that switches themes)

---

## Context to read (only these)

- `US-015-02-theme-parity.md` → happy path, Alternative flows, Edge cases
- `../F-TRF-015-dark-mode.md` → FR-015-2 + Test plan (manual matrix, Playwright asserts)

## Instructions

1. **Parity sweep:** walk every screen in dark mode — landing, upload, sender link/sent, recipient page (all states: active, password card, expired, not-found, download-limit), auth, My Files, profile, admin screens, error pages (F-TRF-013: Ref line, danger chips, toasts). Fix every hardcoded color to its token (`--bg` … `--danger-soft`, UI-Reference §1); components never hardcode colors.
2. **Usage rule:** `--fg-faint` only for placeholders/disabled (UI-Reference §8 contrast rule) — the audit verifies usage sites, not just values; contrast is computed from token values in both palettes, not eyeballed (F-TRF-016 shares this data).
3. **CI token-diff:** a script diffs both `[data-theme]` sets and fails CI on any missing variable per theme (F-TRF-015 test plan) — this is how parity is *maintained* for every screen that ships after T-027.
4. **Playwright:** `emulateMedia({ colorScheme: 'dark' })` + theme setting → assert `data-theme` before paint + screenshot assert on background color (F-TRF-015 test plan).

## Exit check

- [ ] Manual matrix: all screens in both themes — no white flash, no unthemed panel, no unreadable contrast (AC-015-4; T-027 exit "No flash-of-wrong-theme")
- [ ] Recipient page states each render as themed layouts — active, password card, expired, not-found, download-limit
- [ ] 500 error page + admin overview render dark tokens: icon, danger color, Ref line readable (AC-015-4; EC-015-3)
- [ ] CI token-diff passes for both sets — no token missing in either

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS).
Task T-015-03 — full-screen theme parity audit + token-diff CI check (milestone task T-027, F-TRF-015).
Read first (only): docs/AGENT.md; docs/features/Phase 0-MVP/F-TRF-015/US-015-02-theme-parity/US-015-02-theme-parity.md (happy path, Alternative flows, Edge cases); F-TRF-015-dark-mode.md (FR-015-2 + Test plan).
Do exactly: run the dark-mode sweep over every screen (landing, upload, sender link/sent, recipient all states, auth, My Files, profile, admin, error pages) replacing hardcoded colors with UI-Reference §1 tokens; enforce --fg-faint usage only for placeholders/disabled; add the CI script that diffs both [data-theme] token sets and fails on any missing variable per theme; add the Playwright check (emulateMedia colorScheme dark + data-theme before paint + background screenshot assert).
Done when: AC-015-4 holds — no white flash, unthemed panel, or unreadable contrast on any screen in either theme (FR-015-2), and the token-diff script is green for both sets.
Constraints: tokens only; browser forced-colors/OS high-contrast out of scope for MVP (documented); admin uses the same mechanism — no per-area themes (EC-015-3). Commit "feat: T-015-03 {one line}".
```
