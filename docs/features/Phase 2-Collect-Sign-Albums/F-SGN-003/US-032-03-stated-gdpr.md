# US-032-03 — Know exactly what data we keep

**Feature:** F-SGN-003 — Audit Trail | **Status:** pending

---

**Story:** As a signer (a data subject), I want the product to say in plain language what it records about my view and signature — and that no raw IP is kept — so that my consent is informed, not legalistic.
**Actor:** signer, sender, regulator.
**Goal:** one plain-language statement, shown where the data is collected — the sign page context and the sender's tracking view — in every UI language (F-TRF-014).

## Preconditions

- The audit trail exists (F-SGN-003); the i18n strings exist per locale.

## Happy path

1. The sign page (and the sign email's link target) shows: "We log each view and signature with a hashed IP and a timestamp. No raw IP is kept." (FR-032-4)
2. The sender's tracking view shows the same statement as the timeline footer (US-032-01).
3. The statement is a translation file string — never hardcoded, never paraphrased per screen (one canonical string, F-TRF-014).

## Alternative flows

- **Locale**: the statement renders in the viewer's locale; the *substance* (hashed IP + timestamp, no raw IP) is identical in every language (the translation test encodes the three facts).
- **No geo/privacy mode**: the statement still holds — "hashed IP" becomes "no IP" in the row data, but the statement is the *capability* statement, not a per-row claim (documented).

## Acceptance criteria

```gherkin
Given I open the sign page
When I look for the privacy statement
Then the plain-language statement about hashed IP and timestamp is present

Given the same statement in two locales
When compared
Then both name the hashed IP, the timestamp, and the absence of raw IP
```

## Edge cases

- The statement is *not* a cookie banner — it's a footer note, visible without scrolling on the sign page (FR-032-4 placement).
- Changing the statement = a product change: versioned in the translation files with a changelog line (D-22 notes the retention number is pending — the statement must not name a day count until D-22 lands).

## UI notes

- `--fs-tiny`, `--fg-muted` footer under the timeline and on the sign page — visible, not loud.
- Icons optional; the text is the contract.

## Technical notes

- i18n key e.g. `sign.audit.gdpr` — single key, two placements (sign page, tracking footer).
- Retention number: intentionally absent from the string until `DOCUMENT_RETENTION_DAYS` is decided (D-22) — avoids shipping "90 days" we can't keep.

## Links

- Feature: `SGN-003-audit-trail.md` (FR-032-4, FR-032-6)
- Related: US-032-02 (what the statement scopes), F-TRF-014 (i18n), F-PRF-003 (same no-raw-IP pattern)
