# F-TRF-014 — i18n

**Priority:** P1 (foundation: build from day one) | **Phase:** 0 — Core Transfer
**Spec source:** `02-feature-plan.md` F-TRF-014 | **Architecture:** TA-8.5, TA-6.7 (email i18n), TA-11 (ADR-011)
**Milestone tasks:** T-026

---

## Description

From day one, **every user-facing string lives in translation files** (a lint rule, not a hope): the UI is one `i18next` resource bundle per locale, and email templates are per-language files. Locale detection is automatic (browser → account setting → English), dates and numbers are localized, and emails go out in the recipient's language. Launch language set per D-16 (en, nl, de, tr at launch, rest follow).

**Actors:** every user, in their language.
**Value:** the product feels local in 8 markets with zero per-market code.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-014-1 | Languages: en (default), nl, fr, es, pt, it, de, tr (D-16 governs the launch subset). |
| FR-014-2 | UI strings **100% from translation files** — no hardcoded literals (eslint rule `no-restricted-syntax` on JSX string children / `t()` usage; the rule fails CI). |
| FR-014-3 | Locale detection: **browser → account setting → default en** (for signed-in users the account setting wins over the browser). |
| FR-014-4 | Dates/numbers localized via `Intl` (ICU data shipped with the locale); bytes via `formatBytes()` (1024-based, "GB", TA-8.5). |
| FR-014-5 | Emails localized per recipient (CH per-recipient attribute / stored preference); template set `templates/transfer/{lang}.html|.txt` with `en` fallback (TA-6.7, F-TRF-006, US-006-05). |

## Acceptance criteria

```gherkin
AC-014-1: German browser, fresh visit
  Then the entire UI is German

AC-014-2: A translation key is missing
  Then the English fallback is shown and a "translation_missing" event is logged
  And the UI never renders a blank string

AC-014-3: A signed-in user with account locale "nl" and a German browser
  When the app loads
  Then Dutch is used (account setting wins)

AC-014-4: Date "2026-08-23" with locale de
  Then it renders "23.08.2026" (locale-correct)
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-014-1 | Missing translation in a non-en locale | English fallback + `translation_missing` telemetry (key + locale); dev console warn (TA-8.5) |
| EC-014-2 | Plurals/interpolations | i18next `{{count}}` + plural suffixes per locale; byte sizes use the localized number inside `formatBytes` (the unit stays "GB" — documented choice) |
| EC-014-3 | RTL | None of the 8 languages is RTL — no layout flip in MVP (documented) |
| EC-014-4 | Email locale unknown | `en` template (US-006-05) |

## UI notes

- Language switcher: profile (signed-in) — `Select language` (stored on the account); guest: browser-detected, no switcher on the landing (kept minimal per product analysis).
- All strings tone per UI-Reference §7 (calm, no exclamation points) — translation memory keeps the tone across locales.

## Technical notes

- Stack: `i18next` + `react-i18next` (TA-8.1); keys `{area}.{screen}.{control}` (TA-8.5); locale files `core/i18n/locales/{lang}.json` (flat-ish nesting per key scheme).
- Detection order (FR-014-3): `localStorage` account-locale override → `navigator.language` → `en`; resolution in `core/i18n` before first render (no flash of wrong language for the *language*, theme has its own pre-paint path, F-TRF-015).
- `Intl` formatters with the resolved locale; ICU data per locale (bundle size budget TA-8.4 — en/nl/de/tr at launch keep it small per D-16).
- Emails: template selection per US-006-05; CH per-recipient attributes (ADR-011).
- Telemetry: `translation_missing` (key, locale) (TA-10.2).

## Test plan

- Unit: `formatBytes` per locale; plural helpers; key-scheme lint (CI: no raw string literals in features).
- Integration (web): `t()` missing key → fallback + event; locale resolution order (account > browser > en).
- E2E: AC-014-1 (de browser, full flow in German); AC-014-3 (account override); email template rendering per locale (T-019/T-026).
- Lint: eslint rule blocks new hardcoded strings in `features/` (T-026 exit check: "no hardcoded strings (lint)").

## User stories

| ID | Story | File |
|---|---|---|
| US-014-01 | Use the product in my language | `US-014-01-localized-ui.md` |
| US-014-02 | Have the right language automatically | `US-014-02-locale-detection.md` |
| US-014-03 | Get emails in my language | `US-014-03-localized-email.md` |
