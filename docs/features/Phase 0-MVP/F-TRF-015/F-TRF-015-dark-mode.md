# F-TRF-015 — Dark Mode

**Priority:** P1 | **Phase:** 0 — Core Transfer
**Spec source:** `02-feature-plan.md` F-TRF-015 | **Architecture:** TA-8.2 (`core/theme`), UI-Reference §1
**Milestone tasks:** T-027

---

## Description

Theme is a first-class setting: **system | light | dark**. Every screen is themed — including admin and error screens — and the correct theme is applied **before first paint** (inline `data-theme` bootstrap), so there is no flash of the wrong theme. Tokens (UI-Reference §1) carry both palettes; components never hardcode colors.

**Actors:** every user (guests: system by default; signed-in: their profile choice, F-TRF-008-4).
**Value:** dark mode is a top-3 "make it feel like my computer" feature; the no-flash rule is what separates polish from accident.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-015-1 | Theme = `system` \| `light` \| `dark`. Signed-in: stored on the account (`AppUser.Theme`, F-TRF-008-4). Guests: `system` (respects the OS preference live). |
| FR-015-2 | **Full parity:** every screen (landing, link, recipient, My Files, auth, profile, admin, error pages) renders in both themes. |
| FR-015-3 | **No flash of wrong theme:** an inline `data-theme` is set on `<html>` before first paint (bootstrap script reads the stored/preference value; TA-8.2 `core/theme`). |
| FR-015-4 | `system` follows the OS live (a `prefers-color-scheme` change while the tab is open switches the theme without a reload). |

## Acceptance criteria

```gherkin
AC-015-1: A user sets theme = dark in the profile
  When any page loads (or refreshes)
  Then dark tokens are applied before first paint (no light flash)

AC-015-2: Guest with OS in dark mode
  When the landing page loads
  Then dark tokens are applied

AC-015-3: A user in system mode changes their OS theme while the app is open
  When the OS preference changes
  Then the app switches theme without a reload

AC-015-4: Every screen in the app
  When rendered in dark mode
  Then no white flash, no unthemed panel, no unreadable contrast (tokens only, UI-Reference §1)
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-015-1 | Profile theme for a signed-in user + OS change | Account setting wins (the user explicitly chose); `system` is the only mode that tracks the OS |
| EC-015-2 | `prefers-reduced-motion` | Independent of theme: disables animation (UI-Reference §3, F-TRF-016) |
| EC-015-3 | Admin in a different theme than the product | Same mechanism, no per-area themes |

## UI notes (UI-Reference §1)

- Token pairs are the single source: `--bg`, `--bg-subtle`, `--bg-elevated`, `--fg`, `--fg-muted`, `--fg-faint`, `--accent`, `--accent-hover`, `--accent-soft`, `--border`, `--success`, `--warning`, `--danger`, `--danger-soft` — dark column defined in §1.
- Theme select on the profile (UI-Reference §5.5); no inline theme toggle on the landing (kept minimal).
- Elevation rule differs per theme (light: 1px shadow; dark: 1px border, no shadow) — tokens handle it.

## Technical notes

- `core/theme`: bootstrap (inline in `index.html` or a 0-dep pre-paint script): reads account theme (from `/auth/me` cached in `localStorage`) or `matchMedia('(prefers-color-scheme: dark)')` → sets `data-theme="dark|light"` on `<html>`.
- `tokens.css` (TA-8.2 `styles/`) maps tokens per `data-theme`; Tailwind config consumes the CSS variables.
- Live OS tracking: `matchMedia` listener only while in `system` mode.

## Test plan

- Unit: bootstrap resolution (account > preference > default); token sets (no missing variable per theme — a script diffs both sets).
- E2E (Playwright): `emulateMedia({ colorScheme: 'dark' })` + theme setting → `data-theme` correct before paint (screenshot assert on background color); OS-change listener (AC-015-3).
- Manual matrix: all screens in both themes (T-027 exit: "No flash-of-wrong-theme").

## User stories & implementation tasks

| ID | Story / Task | File |
|---|---|---|
| US-015-01 | Choose light, dark, or system theme | `US-015-01-theme-choice/US-015-01-theme-choice.md` |
| US-015-02 | See a consistent theme on every screen | `US-015-02-theme-parity/US-015-02-theme-parity.md` |

**Implementation tasks:** one file per task — each story folder holds its story .md + its task files (context-friendly; execute top-to-bottom).

| Story | Task | File | Status |
|---|---|---|---|
| US-015-02 | T-015-01 Theme tokens + pre-paint bootstrap: both palettes in `tokens.css`, `core/theme` resolution (account > preference > default) (FR-015-1/3, TA-8.2) | `US-015-02-theme-parity/T-015-01-tokens-and-bootstrap.md` | ☐ |
| US-015-01 | T-015-02 Profile theme select + live `system` OS tracking via matchMedia (FR-015-4) | `US-015-01-theme-choice/T-015-02-profile-theme-select-live-system.md` | ☐ |
| US-015-02 | T-015-03 Full-screen theme parity audit + token-diff CI check (FR-015-2) | `US-015-02-theme-parity/T-015-03-parity-audit-and-token-diff.md` | ☐ |

**Story done when:** all tasks checked + full AGENT.md §4 gate green + the story's ACs verified (AC-015-1…AC-015-4, EC-015-1…EC-015-3). Then T-027 can be marked `done` in `Milestone-Backlog.md` — its exit is "No flash-of-wrong-theme" plus the screen-by-screen dark pass (the manual matrix of F-TRF-015's test plan, T-015-03).
