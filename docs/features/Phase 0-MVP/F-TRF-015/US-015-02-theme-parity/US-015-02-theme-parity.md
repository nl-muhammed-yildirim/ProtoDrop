# US-015-02 — See a consistent theme on every screen

**Feature:** F-TRF-015 — Dark Mode | **Status:** pending

---

**Story:** As a user who enabled dark mode, I want *every* screen — not just the landing — to be dark and readable, so that the theme feels like a property of the product, not a lucky page.
**Actor:** Any user (guest or signed-in), on any screen, in either theme.
**Goal:** full parity (FR-015-2): landing, link screen, recipient page (all states), auth, My Files, profile, **admin**, and **error pages** render with the theme tokens — no unthemed panel, no white flash, no unreadable contrast.

## Preconditions

- The theme is resolved (US-015-01) and tokens are the single source (UI-Reference §1 token pairs).

## Happy path

1. User walks the whole app in dark mode: landing → upload → link screen → send → (recipient opens the link) → My Files → profile → an admin screen → an error screen.
2. Every surface uses the dark token set: `--bg`, `--bg-subtle`, `--bg-elevated`, `--fg`, `--fg-muted`, `--fg-faint`, `--accent`, `--accent-hover`, `--accent-soft`, `--border`, `--success`, `--warning`, `--danger`, `--danger-soft` — no component hardcodes a color.
3. Both themes are complete: a script diffs both token sets and fails CI on any missing variable per theme (F-TRF-015 test plan).
4. Error states and degraded states (F-TRF-013) are themed too — the `Ref:` line, danger chips, and toasts all read in both palettes.

## Alternative flows

- **Admin logged in with a different theme choice:** the admin sees *their* theme — same mechanism, no per-area themes (EC-015-3).
- **Theme switch mid-session (profile select):** applies on the next navigation/re-render without a manual reload of already-open modals being required for the main surface (in-place update is acceptable; a full reload is the documented fallback).
- **Browser `forced-colors` / OS high-contrast:** out of scope for MVP (tokens only — documented).

## Acceptance criteria

```gherkin
  When rendered in dark mode
  Then no white flash, no unthemed panel, no unreadable contrast (tokens only, UI-Reference §1)

Given the dark and light token sets
When the CI token-diff script runs
Then no token is missing in either set

Given a user in dark mode
When they visit an error page (500) and the admin overview
Then both render with the dark tokens (icon, danger color, ref line readable)
```

## Edge cases

- The recipient page's **all** states get the parity check: active, password card, expired, not-found, download-limit (each is a themed layout, not a theme-exception).
- `--fg-faint` (the weakest token) is only ever used for placeholders/disabled (UI-Reference §8 contrast rule) — the audit verifies usage sites, not just values.
- Contrast is computed from the token values (both palettes), not eyeballed (F-TRF-016-4 shares this data — F-TRF-016 test plan).

## UI notes

- Token pairs are the single source (UI-Reference §1); the dark column is defined there.
- The manual exit check for T-027 is "no flash-of-wrong-theme" plus a screen-by-screen dark pass (F-TRF-015 test plan: "all screens in both themes").

## Technical notes

- Playwright: `emulateMedia({ colorScheme: 'dark' })` + theme setting → assert `data-theme` before paint + screenshot assert on background color (F-TRF-015 test plan).
- Any new screen that ships must pass the token-diff + the manual matrix — that's how parity is *maintained*, not just achieved once.

## Links

- Feature: `TRF-015-dark-mode.md` (FR-015-2, AC-015-4, EC-015-3)
- Related: US-015-01 (the choice), US-013-01 (themed error screens), US-016-03 (contrast/focus in both themes)
- Architecture: TA-8.2, UI-Reference §1
- Milestone: T-027
