# T-087 — Have the right language automatically

**Story:** US-014-02 | **Feature:** F-TRF-014 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-014/US-014-02-locale-detection.md`
**Coarse task (Milestone-Backlog.md):** T-026
**Status:** pending

---

## Scope

As a user, I want the product to just know my language — no setup, no flag picker on the landing — and, if I'm signed in, to respect the language I deliberately chose in my profile.

**Actor:** Guest (browser-detected) or signed-in user (account setting wins).

**Goal:** detection order `account setting → browser → en` (FR-014-3), resolved before first render.

Happy path:

1. **Guest, browser language `de`:** the app resolves to German before first render — no flash of English, no flash of the wrong language.
2. **Signed-in user, account locale `nl`, browser German:** the app renders **Dutch** — the account setting wins over the browser (AC-014-3, feature spec).
3. **Browser language unsupported (e.g. `ja`):** resolution falls through to `en` (the default, FR-014-3) — silently, no "language not available" ceremony.
4. **Guest later signs up:** the account locale is initialized from the detected language (one-time), after which the profile setting governs (F-TRF-008-4).

## Acceptance criteria

```gherkin
  When the app loads
  Then Dutch is used (account setting wins over the browser)

Given a guest with browser language "de"
When the app loads for the first time
Then German is resolved before first render (no flash of another language)

Given a guest with an unsupported browser language
When the app loads
Then English (the default) is used
```

## Edge cases

- Resolution must be **synchronous and cheap** (read `localStorage` / cached `/auth/me` — no network await on the critical path beyond what sign-in already did).
- The pre-render resolution is distinct from the theme pre-paint path (F-015): both run before paint, but they own separate decisions — no cross-coupling.
- Detection is per-browser for guests; per-account for signed-in users. A user on two browsers sees two languages if their browser settings differ (guest mode) — expected, documented.

## Exit check

- [ ] Scenario 1: a guest with browser language "de"
- [ ] Scenario 2: a guest with an unsupported browser language
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-014/US-014-02-locale-detection.md`
- Feature: `TRF-014-i18n.md` (FR-014-3, AC-014-3)
- Related: US-014-01 (rendering in the resolved language), US-014-03 (email locale — separate mechanism, per-recipient)
- Architecture: TA-8.5
- Milestone: T-026
