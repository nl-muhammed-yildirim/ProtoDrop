# T-014-03 — string sweep: all UI strings via t() + no-hardcoded-string lint

**Story:** US-014-01-localized-ui
**Spec:** F-TRF-014-i18n.md
**Size:** M
**Depends on:** T-014-01 (bundles/keys), T-014-02 (detection — so AC-014-1 is testable end-to-end)

## Context to read (only these)

- docs/features/Phase 0-MVP/F-TRF-014/F-TRF-014-i18n.md — FR-014-2, FR-014-4, AC-014-1/2/4, EC-014-1/2/3
- docs/features/Phase 0-MVP/F-TRF-014/US-014-01-localized-ui/US-014-01-localized-ui.md — Happy path + Alternative flows + Edge cases + Technical notes
- docs/03-technical-architecture.md — "TA-8.5 i18n" (missing-key fallback, formatBytes)

## Instructions

1. Sweep every P0 surface (landing, link screen, recipient page, auth, My Files, profile): move each user-facing string literal into the locale bundles as a `{area}.{screen}.{control}` key and render via `t()`. Target: 100% of UI strings from translation files — zero hardcoded literals (FR-014-2).
2. Missing-key behavior (AC-014-2 / EC-014-1): a key absent in a non-en locale renders the **English fallback** (never blank) + logs a `translation_missing` event (key + locale, TA-10.2) + dev console warn.
3. Dates/numbers/bytes via `Intl` with the resolved locale (FR-014-4): date "2026-08-23" under `de` → "23.08.2026" (AC-014-4); byte sizes use `formatBytes()` (1024-based) with the localized number but the unit stays **"GB"** in all locales (EC-014-2, documented choice).
4. Plurals/interpolation per locale via i18next (`{{count}}` + plural suffixes — EC-014-2); parameters interpolate inside the bundle, never concatenated.
5. Add the eslint `no-restricted-syntax` rule on raw string literals in JSX / `t()` usage that **fails CI** (FR-014-2; T-026 exit check "no hardcoded strings (lint)"). RTL: none of the 8 languages is RTL — no layout flip in MVP (EC-014-3, documented).

DECISION: the lint rule targets `features/` raw JSX string children / non-`t()` usage per FR-014-2's wording; the exact rule config is not pinned in Open-Decisions-and-Constants.md Part 2.
ASSUMPTION: email-side copy is excluded from this client lint (it lives in `templates/transfer/{lang}.*`, enforced by T-014-04 / TA-6.7) — confirm the lint scope boundary before CI wiring.

## Exit check

- [ ] AC-014-1 — German browser, fresh visit → entire UI is German (no English residue, no missing-key blanks)
- [ ] AC-014-2 — a missing translation key renders the English fallback + logs `translation_missing` (key + locale); UI never shows a blank string
- [ ] EC-014-1 — same as AC-014-2 with dev console warn; telemetry carries key + locale (TA-10.2)
- [ ] AC-014-4 — date "2026-08-23" under `de` renders "23.08.2026" via Intl (FR-014-4)
- [ ] EC-014-2 — plurals/interpolation correct per locale; `formatBytes()` localizes the number, unit stays "GB"
- [ ] EC-014-3 — no RTL layout flip for any of the 8 languages (documented)
- [ ] FR-014-2 — a hardcoded JSX string in `features/` fails the eslint lint gate in CI

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS).
Task T-014-03 — client string sweep + no-hardcoded-string lint (milestone task T-026, F-TRF-014, TA-8.5).
Read first (only): docs/AGENT.md; docs/features/Phase 0-MVP/F-TRF-014/F-TRF-014-i18n.md (FR-014-2, FR-014-4, AC-014-1/2/4, EC-014-1/2/3); US-014-01-localized-ui.md (Happy path + Alternative flows + Edge cases + Technical notes); docs/03-technical-architecture.md TA-8.5 i18n.
Do exactly: move all P0-surface user-facing strings into locale bundles as {area}.{screen}.{control} keys and render via t(); missing key -> English fallback + translation_missing telemetry (key+locale) + dev warn; dates/numbers/bytes via Intl with formatBytes (unit stays "GB"); per-locale plurals/interpolation; add the eslint no-restricted-syntax rule that fails CI on hardcoded JSX strings in features/.
Done when: AC-014-1, AC-014-2, AC-014-4 and EC-014-1, EC-014-2, EC-014-3 hold, and a new hardcoded string fails CI.
Constraints: depends on T-014-01 + T-014-02; no new error codes or limits; email copy out of scope (T-014-04). Commit "feat: T-014-03 {one line}".
```
