# T-058 — Receive the email in my language

**Story:** US-006-05 | **Feature:** F-TRF-006 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-006/US-006-05-localized-email.md`
**Coarse task (Milestone-Backlog.md):** T-019, T-026
**Status:** pending

---

## Scope

As a recipient in a supported locale, I want the transfer email in my language, so that "Download files" is a button I actually understand.

**Actor:** Recipient whose address previously sent us an `Accept-Language` hint — or, at MVP, whose address matches a stored preference.

**Goal:** The template is rendered in the recipient's language, with English as the fallback.

Happy path:

1. `f-email` resolves the recipient's language: stored per-recipient attribute (Communication Hub) → default `en`.
2. The matching template renders; subject, body, CTA, and footer are localized.
3. Dynamic values (sender name, file names, sizes, expiry days) are formatted with locale-aware formatters (`Intl`-equivalent on the C# side).

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

## Exit check

- [ ] Scenario 1: a recipient marked with language "de"
- [ ] Scenario 2: a recipient with no language preference
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-006/US-006-05-localized-email.md`
- Feature: `TRF-006-email.md` (localization of FR-006-2)
- Related: US-014-03 (same requirement from the i18n feature), F-TRF-014-5
- Architecture: TA-6.7, TA-8.5
- Milestone: T-019, T-026
