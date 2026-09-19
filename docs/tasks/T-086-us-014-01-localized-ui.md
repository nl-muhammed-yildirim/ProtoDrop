# T-086 — Use the product in my language

**Story:** US-014-01 | **Feature:** F-TRF-014 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-014/US-014-01-localized-ui.md`
**Coarse task (Milestone-Backlog.md):** T-026
**Status:** pending

---

## Scope

As a user whose language is one of the supported ones (en, nl, fr, es, pt, it, de, tr — FR-014-1), I want every screen to be in my language, so that the product feels local without me installing anything.

**Actor:** Any user (guest or signed-in), on any P0 surface.

**Goal:** 100% of UI strings come from translation files — zero hardcoded literals — and dates, numbers, and byte sizes render locale-correct.

Happy path:

1. User with a German browser (or `de` account setting) opens any P0 surface: landing, link screen, recipient page, auth, My Files, profile.
2. Every string is German: buttons, helpers, empty states, error copy, toasts — no English residue, no missing-key blanks.
3. Dates render locale-correct (AC-014-4: "2026-08-23" → "23.08.2026" under `de`); numbers use the locale's grouping/decimal; byte sizes use `formatBytes()` with a localized number but the unit stays "GB" (EC-014-2, documented choice).
4. Plurals and interpolations are correct per locale (`{{count}}` + plural suffixes via i18next, EC-014-2) — e.g. "N files · X GB" in its localized form.

## Acceptance criteria

```gherkin
  Then the entire UI is German

Given the locale is de
When the date 2026-08-23 is rendered
Then it shows "23.08.2026" (locale-correct via Intl)

Given a translation key is missing in locale fr
When the component renders
Then the English fallback text is shown (never a blank string)
And a "translation_missing" event is logged with the key and locale
```

## Edge cases

- The CI lint rule is the *enforcement* mechanism: `no-restricted-syntax` on raw string literals in `features/` — a PR that hardcodes a string fails CI (FR-014-2, T-026 exit check "no hardcoded strings (lint)").
- Byte units ("GB") stay Latin in all locales (EC-014-2) — a conscious, documented trade-off; the *number* is localized.
- Email-side locale handling is a separate story (US-014-03); this story covers the UI bundle only.

## Exit check

- [ ] Scenario 1: the locale is de
- [ ] Scenario 2: a translation key is missing in locale fr
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-014/US-014-01-localized-ui.md`
- Feature: `TRF-014-i18n.md` (FR-014-1, FR-014-2, FR-014-4, AC-014-1, AC-014-2, AC-014-4)
- Related: US-014-02 (locale resolution), US-014-03 (email locales)
- Architecture: TA-8.1, TA-8.5
- Milestone: T-026
