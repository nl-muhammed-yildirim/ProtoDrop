# T-083 — Understand an error and know what to do

**Story:** US-013-01 | **Feature:** F-TRF-013 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-013/US-013-01-error-screens.md`
**Coarse task (Milestone-Backlog.md):** T-025
**Status:** pending

---

## Scope

As any user who hit an error, I want to see one line that explains the situation and one button that tells me what to do — plus a reference I can quote to support — so that the error explains itself instead of trapping me.

**Actor:** Any user (sender or recipient), any screen.

**Goal:** every error state renders as the brand-consistent single-screen layout (UI-Reference §4.6): icon, one line, one action, `Ref: {id8}`.

Happy path:

1. An error occurs (e.g. the API returns a 500 Problem+JSON with a `code`).
2. The UI maps the `code` to its screen/state (FR-013-6): known codes → their dedicated screens (F-TRF-003 states, limit screens, `WRONG_PASSWORD` shake, `RATE_LIMITED` hint); unknown codes → the generic 500 screen.
3. The 500 screen shows: the `alert` icon, one line ("Something went wrong."), a **Try again** button, and `Ref: a1b2c3d4` in `--fs-tiny` `--fg-muted` (FR-013-1, FR-013-2).
4. The same 8-hex `Ref` is the W3C trace id's prefix — the operator finds the full log in Application Insights by that ref (F-TRF-012-2).

## Acceptance criteria

```gherkin
  Then the user sees "Ref: a1b2c3d4" and a Try again button
  And the error is in Application Insights under that ref

Given an unknown route is opened
When the SPA renders
Then the 404 screen shows with one action (Go to home)
And no stack trace or raw error code is visible

Given the API returns a known error code
When the UI renders
Then the mapped dedicated screen/state is shown (per the F-TRF-013 code map)
And unknown codes fall back to the generic 500 screen
```

## Edge cases

- **Ref id reuse:** 8 hex of a W3C trace id is unique per request in practice; two 500s in the same tab still get distinct refs (EC-013-1).
- **i18n:** every error string is a translation key; "Ref:" is localized, the id is not (EC-013-3, F-TRF-014-2).
- **The Try again button** re-issues the same action (retry the upload block / re-fetch the page) — it is not a full page reload unless the action was a page load.

## Exit check

- [ ] Scenario 1: an unknown route is opened
- [ ] Scenario 2: the API returns a known error code
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-013/US-013-01-error-screens.md`
- Feature: `TRF-013-errors.md` (FR-013-1, FR-013-2, FR-013-5, FR-013-6, AC-013-1)
- Related: US-012-01 (correlation ids), US-003-05 (terminal-state screens)
- Architecture: TA-4.1.3, TA-10.5, TA-10.2
- Design: UI-Reference §4.6
- Milestone: T-025
