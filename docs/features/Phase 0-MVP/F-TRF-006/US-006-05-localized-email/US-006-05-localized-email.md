# US-006-05 — Receive the email in my language

**Feature:** F-TRF-006 — Email Notifications | **Status:** pending

---

**Story:** As a recipient in a supported locale, I want the transfer email in my language, so that "Download files" is a button I actually understand.
**Actor:** Recipient whose address previously sent us an `Accept-Language` hint — or, at MVP, whose address matches a stored preference.
**Goal:** The template is rendered in the recipient's language, with English as the fallback.

## Preconditions

- Supported UI languages: en, nl, fr, es, pt, it, de, tr (F-TRF-014-1); launch subset per D-16.
- Templates exist per language: `templates/transfer/{lang}.html` + `.txt`.

## Happy path

1. `f-email` resolves the recipient's language: stored per-recipient attribute (Communication Hub) → default `en`.
2. The matching template renders; subject, body, CTA, and footer are localized.
3. Dynamic values (sender name, file names, sizes, expiry days) are formatted with locale-aware formatters (`Intl`-equivalent on the C# side).

## Alternative flows

- **Language not covered by a template:** fall back to `en` — never a blank/broken email; `translation_missing`-style telemetry (`email.template_fallback`) logged.
- **Unknown recipient preference:** `en` default (privacy: no geolocation in MVP).

## Acceptance criteria

```gherkin
Given a recipient marked with language "de"
When the transfer email is rendered
Then the German template is used (subject, body, CTA, footer)
And file names and sizes are formatted for de

Given a recipient with no language preference
When the transfer email is rendered
Then the English template is used
```

## Edge cases

- Numbers/days: "Dieser Link läuft in 7 Tagen ab." — localized pattern, not string concatenation.
- File names themselves are NOT translated (proper nouns of the data).
- Template missing a variable → Stubble leaves the variable visible; a dev alert (`template_render_error`) fires — caught in dev via the log-sender run.

## UI notes

- Email client reality: language of *email* ≠ language of *web UI* (the web UI detects its own locale, F-TRF-014-2) — both use `en` as fallback independently.

## Technical notes

- CH per-recipient attributes carry the language (F-TRF-014-5, TA-11 ADR-011).
- Template selection: `templates/transfer/{lang}.html`, `en` fallback file (TA-6.7, F-TRF-014-5).
- This story is the email half of F-TRF-014; the web-UI half is US-014-01/02.

## Links

- Feature: `TRF-006-email.md` (localization of FR-006-2)
- Related: US-014-03 (same requirement from the i18n feature), F-TRF-014-5
- Architecture: TA-6.7, TA-8.5
- Milestone: T-019, T-026
