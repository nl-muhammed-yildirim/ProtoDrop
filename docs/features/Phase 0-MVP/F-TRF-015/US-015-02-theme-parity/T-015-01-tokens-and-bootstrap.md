# T-015-01 — Theme tokens + pre-paint bootstrap (TA-8.2 `core/theme`)

**Story:** US-015-02 | **Spec:** FR-015-1/3, AC-015-1, EC-015-1 | **Size:** M
**Depends on:** T-008-06 (PATCH /auth/me already carries `theme`)

---

## Context to read (only these)

- `../US-015-02-theme-parity.md` → happy path 2–3 (token pairs, completeness)
- `../../F-TRF-015-dark-mode.md` → FR-015-1/3 + UI notes (token list) + Technical notes + Test plan
- `docs/03-technical-architecture.md` → TA-8.2 (structure: `core/theme`, `styles/tokens.css`)

## Instructions

1. Define both palettes as the full token pair set in `styles/tokens.css` mapped per `[data-theme="light"]` / `[data-theme="dark"]`: `--bg`, `--bg-subtle`, `--bg-elevated`, `--fg`, `--fg-muted`, `--fg-faint`, `--accent`, `--accent-hover`, `--accent-soft`, `--border`, `--success`, `--warning`, `--danger`, `--danger-soft` (UI-Reference §1 — the dark column is defined there). Elevation rule lives in tokens: light = 1px shadow, dark = 1px border, no shadow.
2. Tailwind config consumes these CSS variables so components reference tokens only.
3. Add the pre-paint bootstrap to `core/theme` (inline script in `index.html` or a 0-dep pre-paint script — TA-8.2): resolution order is **account theme (from `/auth/me` cached in `localStorage`) → `matchMedia('(prefers-color-scheme: dark)')` → `light` default**, then sets `data-theme="dark|light"` on `<html>` before first paint (FR-015-3).
4. Unit-test the resolution order (F-TRF-015 test plan: "bootstrap resolution (account > preference > default)").

ASSUMPTION: US-015-01's edge-case note lists a `localStorage` last-known input between account theme and matchMedia; no named key is grounded in the spec — reuse whatever `/auth/me` cache key exists for session, or add one explicitly here and document it.

## Exit check

- [ ] Both token sets are complete: a script diffs both `[data-theme]` blocks and fails on any missing variable per theme (F-TRF-015 test plan)
- [ ] Bootstrap resolution unit tests pass for account > preference > default; explicit `light`/`dark` accounts do not follow the OS (EC-015-1)
- [ ] With dark resolved, `<html>` carries `data-theme="dark"` before first paint — no light flash (AC-015-1, FR-015-3)
- [ ] Guest on a dark OS loads the landing dark without any API call (AC-015-2, FR-015-1)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS).
Task T-015-01 — theme tokens + pre-paint bootstrap (milestone task T-027, F-TRF-015, TA-8.2 core/theme).
Read first (only): docs/AGENT.md; docs/features/Phase 0-MVP/F-TRF-015/US-015-02-theme-parity/US-015-02-theme-parity.md (happy path 2–3); F-TRF-015-dark-mode.md (FR-015-1/3, UI notes token list, Technical notes, Test plan); docs/03-technical-architecture.md TA-8.2.
Do exactly: define both palettes in styles/tokens.css per data-theme using the 14 token pairs from F-TRF-015's UI notes (UI-Reference §1); Tailwind config consumes the CSS variables; add the pre-paint bootstrap to core/theme (inline or 0-dep script) with resolution account theme (localStorage-cached /auth/me) > matchMedia('(prefers-color-scheme: dark)') > light default, setting data-theme on <html> before first paint; unit-test that resolution order.
Done when: AC-015-1 and AC-015-2 hold — dark resolves before first paint for signed-in-dark accounts and dark-OS guests, the token-diff script finds no missing variable in either set, and the account > preference > default unit tests pass (FR-015-3).
Constraints: tokens only — components never hardcode colors; elevation rule lives in tokens (light shadow / dark border); no new error codes or limits. Commit "feat: T-015-01 {one line}".
```
