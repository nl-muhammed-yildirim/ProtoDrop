# T-088 — Get emails in my language

**Story:** US-014-03 | **Feature:** F-TRF-014 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-014/US-014-03-localized-email.md`
**Coarse task (Milestone-Backlog.md):** T-019 (template machinery), T-026 (locale set)
**Status:** pending

---

## Scope

As a recipient, I want the transfer email to be in my language, so that the first touchpoint of the product already feels like it's written for me.

**Actor:** Recipient (email inbox), any supported language.

**Goal:** `f-email` selects the template per recipient — stored preference / Communication Hub per-recipient attribute — with `en` as the fallback (FR-014-5, TA-6.7).

Happy path:

1. Recipient with a known locale (stored preference or CH per-recipient attribute) receives the email in their language: subject, greeting, file list, the single **Download files** CTA, the expiry footer, and the unsubscribe link — all localized.
2. The locale resolution is *per recipient* in a multi-address send: one send can produce the email in language A for recipient 1 and language B for recipient 2 (FR-006-1's one-send-per-address makes this natural).
3. **Unknown locale:** the `en` template is used — no broken half-translated email, no missing-template failure (EC-014-4).

## Acceptance criteria

```gherkin
Given a recipient whose locale is "de"
When a transfer is sent to them
Then the email is rendered from templates/transfer/de.html (+ .txt)
And the subject, CTA, expiry footer, and unsubscribe link are German

Given a recipient with no known locale
When a transfer is sent to them
Then the email uses the "en" template (no failure, no mixed-language body)

Given a 2-recipient send with locales "nl" and "tr"
When f-email processes the transfer
Then each address receives the template for its own language
```

## Edge cases

- **Template parity:** every locale ships `.html` **and** `.txt` — a locale with only one is treated as incomplete → `en` fallback for that locale (prevents plain-text clients from getting HTML).
- The file list inside the email is locale-formatted (human sizes, FR-006-2) — same `formatBytes` rule as the UI (unit stays "GB", EC-014-2).
- Locale changes **after** an email was sent don't re-send (the email is immutable once dispatched — consistent with the eventual-consistency model, US-013-03).

## Exit check

- [ ] Scenario 1: a recipient whose locale is "de"
- [ ] Scenario 2: a recipient with no known locale
- [ ] Scenario 3: a 2-recipient send with locales "nl" and "tr"
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-014/US-014-03-localized-email.md`
- Feature: `TRF-014-i18n.md` (FR-014-5, EC-014-4)
- Related: US-006-01 (the email itself), US-006-05 (this same story from the email feature's index — keep both in sync)
- Architecture: TA-6.7, ADR-011
- Milestone: T-019 (template machinery), T-026 (locale set)
