# T-006-04 — Localized templates: per-language render + en fallback (F-TRF-014-5)

**Story:** US-006-05 | **Spec:** FR-006-2 (localization), F-TRF-014-5, TA-6.7/TA-8.5 | **Size:** M
**Depends on:** T-006-01 (f-email consumer — this task adds the template set + language resolution)

---

## Context to read (only these)

- `US-006-05-localized-email.md` → happy path + Alternative flows + Edge cases
- `../../F-TRF-006-email.md` → FR-006-2 + Technical notes (templates line)

## Instructions

1. Add the template set **`Emails/templates/transfer/{lang}.html` + `.txt`** for the supported locales (en, nl, fr, es, pt, it, de, tr — F-TRF-014-1; launch subset per D-16). One place, never inline in code (TA-6.7).
2. Language resolution: recipient's stored attribute (Communication Hub) → default **`en`**. Unknown preference or missing template → `en` fallback + **`email.template_fallback`** telemetry — never a blank/broken email.
3. Render subject, body, CTA, and footer in the resolved language; format dynamic values (sender name, file names, sizes, expiry days) with locale-aware formatters (`Intl`-equivalent on the C# side).
4. File names themselves are **not** translated (proper nouns of the data); localized day pattern ("Dieser Link läuft in 7 Tagen ab." — not string concatenation).
5. Template missing a variable → Stubble leaves it visible + dev alert **`template_render_error`** — caught via the log-sender run in dev.

## Exit check

- [ ] Recipient marked "de" → German template (subject, body, CTA, footer) with de-formatted values
- [ ] No preference / missing template → English fallback + `email.template_fallback` logged
- [ ] Unit: rendering for each shipped locale produces no visible Stubble variables

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; f-email consumer in place).
Task T-006-04 — add localized email templates.
Read first (only): docs/features/Phase 0-MVP/F-TRF-006/US-006-05-localized-email/US-006-05-localized-email.md (happy path + Alternative flows + Edge cases) and F-TRF-006-email.md (FR-006-2).
Do exactly:
1. Add the template set Emails/templates/transfer/{lang}.html + .txt for the supported locales (en, nl, fr, es, pt, it, de, tr — F-TRF-014-1; launch subset per D-16). One place, never inline in code (TA-6.7).
2. Resolve language: recipient's stored attribute (Communication Hub) → default en. Unknown preference or missing template → en fallback + email.template_fallback telemetry — never a blank/broken email.
3. Render subject, body, CTA, and footer in the resolved language; format dynamic values (sender name, file names, sizes, expiry days) with locale-aware formatters (Intl-equivalent on the C# side).
4. Keep file names untranslated (proper nouns of the data); use localized day patterns ("Dieser Link läuft in 7 Tagen ab." — not string concatenation).
5. On a template missing a variable: Stubble leaves it visible + dev alert template_render_error — caught via the log-sender run in dev.
Done when: the ACs hold — a "de" recipient gets the German template with de-formatted values, no-preference recipients get English with the fallback telemetry, and every shipped locale renders without visible variables.
Constraints: templates live only in Emails/templates (TA-6.7); email language ≠ web-UI language — both fall back to en independently (F-TRF-014-2/5); this is the email half of F-TRF-014 (web-UI half = US-014-01/02).
```
