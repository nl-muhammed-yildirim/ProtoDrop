# T-090 — See a consistent theme on every screen

**Story:** US-015-02 | **Feature:** F-TRF-015 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-015/US-015-02-theme-parity.md`
**Coarse task (Milestone-Backlog.md):** T-027
**Status:** pending

---

## Scope

As a user who enabled dark mode, I want *every* screen — not just the landing — to be dark and readable, so that the theme feels like a property of the product, not a lucky page.

**Actor:** Any user (guest or signed-in), on any screen, in either theme.

**Goal:** full parity (FR-015-2): landing, link screen, recipient page (all states), auth, My Files, profile, **admin**, and **error pages** render with the theme tokens — no unthemed panel, no white flash, no unreadable contrast.

Happy path:

1. User walks the whole app in dark mode: landing → upload → link screen → send → (recipient opens the link) → My Files → profile → an admin screen → an error screen.
2. Every surface uses the dark token set: `--bg`, `--bg-subtle`, `--bg-elevated`, `--fg`, `--fg-muted`, `--fg-faint`, `--accent`, `--accent-hover`, `--accent-soft`, `--border`, `--success`, `--warning`, `--danger`, `--danger-soft` — no component hardcodes a color.
3. Both themes are complete: a script diffs both token sets and fails CI on any missing variable per theme (F-TRF-015 test plan).
4. Error states and degraded states (F-TRF-013) are themed too — the `Ref:` line, danger chips, and toasts all read in both palettes.

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

## Exit check

- [ ] Scenario 1: the dark and light token sets
- [ ] Scenario 2: a user in dark mode
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-015/US-015-02-theme-parity.md`
- Feature: `TRF-015-dark-mode.md` (FR-015-2, AC-015-4, EC-015-3)
- Related: US-015-01 (the choice), US-013-01 (themed error screens), US-016-03 (contrast/focus in both themes)
- Architecture: TA-8.2, UI-Reference §1
- Milestone: T-027
