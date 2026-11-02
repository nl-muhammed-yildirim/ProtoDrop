# US-014-01 — Use the product in my language

**Feature:** F-TRF-014 — i18n | **Status:** pending

---

**Story:** As a user whose language is one of the supported ones (en, nl, fr, es, pt, it, de, tr — FR-014-1), I want every screen to be in my language, so that the product feels local without me installing anything.
**Actor:** Any user (guest or signed-in), on any P0 surface.
**Goal:** 100% of UI strings come from translation files — zero hardcoded literals — and dates, numbers, and byte sizes render locale-correct.

## Preconditions

- The locale is resolved (US-014-02): browser → account setting → `en`.
- The locale is one of the 8 supported languages (FR-014-1); the launch subset per D-16 is en, nl, de, tr, the rest follow.

## Happy path

1. User with a German browser (or `de` account setting) opens any P0 surface: landing, link screen, recipient page, auth, My Files, profile.
2. Every string is German: buttons, helpers, empty states, error copy, toasts — no English residue, no missing-key blanks.
3. Dates render locale-correct (AC-014-4: "2026-08-23" → "23.08.2026" under `de`); numbers use the locale's grouping/decimal; byte sizes use `formatBytes()` with a localized number but the unit stays "GB" (EC-014-2, documented choice).
4. Plurals and interpolations are correct per locale (`{{count}}` + plural suffixes via i18next, EC-014-2) — e.g. "N files · X GB" in its localized form.

## Alternative flows

- **Missing translation key in a non-en locale:** the English fallback renders (never blank) + a `translation_missing` event (key + locale) + dev console warn (EC-014-1, AC-014-2).
- **Guest vs signed-in:** guests get the browser-detected language (no switcher on the landing, kept minimal per product analysis); signed-in users change the language in the profile (`Select language`, stored on the account — F-TRF-008-4).
- **RTL:** none of the 8 languages is RTL — no layout flip in MVP (EC-014-3, documented).

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

## UI notes

- Language switcher: profile only (signed-in), per UI-Reference §5.5 pattern; guest: no switcher (browser-detected).
- Tone carries across locales: calm, no exclamation points (UI-Reference §7) — the translation memory keeps the voice consistent.

## Technical notes

- Stack: `i18next` + `react-i18next`; keys `{area}.{screen}.{control}`; locale files `core/i18n/locales/{lang}.json` (TA-8.5).
- Resolution happens before first render in `core/i18n` (no flash of the wrong *language*); theme has its own pre-paint path (F-TRF-015) — the two mechanisms don't interfere.
- `Intl` formatters with the resolved locale; ICU data per locale within the bundle budget (TA-8.4; D-16 keeps launch weight small).

## Links

- Feature: `TRF-014-i18n.md` (FR-014-1, FR-014-2, FR-014-4, AC-014-1, AC-014-2, AC-014-4)
- Related: US-014-02 (locale resolution), US-014-03 (email locales)
- Architecture: TA-8.1, TA-8.5
- Milestone: T-026
