# T-014-04 — email template locale selection per recipient (f-email, backend)

**Story:** US-014-03-localized-email
**Spec:** F-TRF-014-i18n.md
**Size:** M
**Depends on:** F-TRF-006 email/template machinery (T-019: `templates/transfer/{lang}.html|.txt` + Stubble); independent of client T-014-01..03

## Context to read (only these)

- docs/features/Phase 0-MVP/F-TRF-014/F-TRF-014-i18n.md — FR-014-5, EC-014-4
- docs/features/Phase 0-MVP/F-TRF-014/US-014-03-localized-email/US-014-03-localized-email.md — Happy path + Alternative flows + Edge cases + Technical notes
- docs/03-technical-architecture.md — "TA-6.7" (email i18n), ADR-011 (CH per-recipient attributes)

## Instructions

1. In `f-email`, select the transfer template **per recipient** from their stored preference / Communication Hub per-recipient attribute (ADR-011): `templates/transfer/{lang}.html` + `.txt`. One multi-address send renders each address in its own language (FR-006-1's one-send-per-address makes this natural).
2. Unknown locale → the **`en`** template — no broken half-translated email, no missing-template failure (EC-014-4 / AC: "no known locale → en").
3. Template parity: every locale ships **both** `.html` and `.txt`; a locale with only one is incomplete → `en` fallback for that locale (prevents plain-text clients getting HTML). Locale stored but template missing (new language not fully shipped) → fall back to `en` + log `email_locale_fallback`.
4. The file list inside the email is locale-formatted via the same `formatBytes()` rule (unit stays "GB", EC-014-2; human sizes FR-006-2). Locale changes after dispatch don't re-send (email immutable once dispatched).
5. Template selection must be **pure** (no side effects) so it's unit-testable per locale. Telemetry: `email.sent` carries the `locale` used; `email_locale_fallback` on any fallback (TA-10.2). The template set is the only place email copy lives — lint/review enforces "no email copy in code" (TA-6.7).

DECISION: locale stored via CH per-recipient attribute or defaults to `en`; the future unsubscribe/profile preference surface is out of MVP scope (documented in US-014-03).
ASSUMPTION: the exact telemetry event name for a stored-but-missing template (`email_locale_fallback`) is not pinned in Open-Decisions-and-Constants.md Part 2 — confirm against TA-10.2's contract before emitting.

## Exit check

- [ ] FR-014-5 — recipient with locale `de` gets the email from `templates/transfer/de.html` (+ `.txt`): subject, greeting, file list, Download files CTA, expiry footer, unsubscribe link all German
- [ ] EC-014-4 — recipient with no known locale → `en` template (no failure, no mixed-language body)
- [ ] AC (US-014-03 gherkin) — a 2-recipient send with locales "nl" and "tr" → each address receives the template for its own language (per-recipient selection)
- [ ] Template parity — a locale shipping only `.html` or only `.txt` → `en` fallback for that locale; stored-but-missing template → `en` + `email_locale_fallback` logged
- [ ] File list locale-formatted via formatBytes (unit "GB", EC-014-2); telemetry `email.sent` carries the locale used

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS).
Task T-014-04 — email template locale selection per recipient in f-email (milestone task T-026, F-TRF-014; template machinery from T-019).
Read first (only): docs/AGENT.md; docs/features/Phase 0-MVP/F-TRF-014/F-TRF-014-i18n.md (FR-014-5, EC-014-4); US-014-03-localized-email.md (Happy path + Alternative flows + Edge cases + Technical notes); docs/03-technical-architecture.md TA-6.7 + ADR-011.
Do exactly: in f-email select templates/transfer/{lang}.html|.txt per recipient from stored preference / CH per-recipient attribute (ADR-011), pure selection unit-testable per locale; unknown/missing/incomplete template -> en fallback + email_locale_fallback telemetry; file list via formatBytes (unit "GB"); email.sent carries the locale used.
Done when: FR-014-5, EC-014-4 and the US-014-03 gherkin (de recipient -> de.html; no locale -> en; nl+tr two-address send each gets its own language) hold, with template parity enforcing .html+.txt or en.
Constraints: backend f-email only; depends on T-019 template set; no new error codes or limits; commit "feat: T-014-04 {one line}".
```
