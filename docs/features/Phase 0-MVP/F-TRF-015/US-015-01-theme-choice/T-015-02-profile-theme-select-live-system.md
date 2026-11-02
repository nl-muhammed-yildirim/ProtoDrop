# T-015-02 — Profile theme select + live `system` OS tracking (FR-015-4)

**Story:** US-015-01 | **Spec:** FR-015-1/4, AC-015-3, EC-015-1/2/3 | **Size:** S
**Depends on:** T-015-01 (tokens + bootstrap — this task writes the value it resolves)

---

## Context to read (only these)

- `US-015-01-theme-choice.md` → happy path, Alternative flows, Edge cases
- `../US-015-02-theme-parity/T-015-01-tokens-and-bootstrap.md` → where the resolution order lives
- `../../F-TRF-015-dark-mode.md` → FR-015-1/4 + UI notes (theme select)

## Instructions

1. Profile screen (UI-Reference §5.5): theme select with **system / light / dark**, current value marked; save via the existing `PATCH /auth/me` (`UpdateProfileCommand` already accepts `theme` — T-008-06). No inline theme toggle on the landing (kept minimal).
2. On save: persist the resolved account theme to the same `localStorage` cache `/auth/me` populates, so the next paint's bootstrap reads it (FR-015-3); switch the live `data-theme` in place — a full reload is the documented fallback if in-place update fails (US-015-02 Alternative flows).
3. Live OS tracking: register the `matchMedia('(prefers-color-scheme: dark)')` listener **only while in `system` mode** (FR-015-4, Technical notes) — an OS change with the tab open switches the theme without a reload; explicit `light`/`dark` never follows (EC-015-1).
4. Admin screens use the same mechanism — no per-area themes (EC-015-3).

## Exit check

- [ ] Select round-trips: pick Dark → PATCH /auth/me persists → profile reflects the choice; guest still gets `system` (FR-015-1)
- [ ] OS theme change while in `system` mode switches the app without a reload (AC-015-3, FR-015-4)
- [ ] User in explicit `light`/`dark` does not follow OS changes — account setting wins (EC-015-1); after picking Dark, any page load applies dark before first paint (AC-015-1)
- [ ] `prefers-reduced-motion` is untouched by theme code (EC-015-2)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS).
Task T-015-02 — profile theme select + live system OS tracking (milestone task T-027, F-TRF-015).
Read first (only): docs/AGENT.md; docs/features/Phase 0-MVP/F-TRF-015/US-015-01-theme-choice/US-015-01-theme-choice.md (happy path, Alternative flows, Edge cases); US-015-02-theme-parity/T-015-01-tokens-and-bootstrap.md; F-TRF-015-dark-mode.md (FR-015-1/4).
Do exactly: add the theme select (system/light/dark) to the profile screen per UI-Reference §5.5, saving via the existing PATCH /auth/me UpdateProfileCommand (theme already accepted); persist the resolved value where T-015-01's bootstrap reads it; switch data-theme in place after save (full reload documented fallback); add the matchMedia('(prefers-color-scheme: dark)') listener active only while mode is system.
Done when: AC-015-3 and EC-015-1 hold — OS changes flip the app live in system mode, explicit choices ignore the OS, and a subsequent page load resolves to the stored theme before first paint (AC-015-1).
Constraints: no new API surface (reuse PATCH /auth/me); listener registered only in system mode; no inline landing toggle. Commit "feat: T-015-02 {one line}".
```
