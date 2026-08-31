# US-031-02 — Be sure of the moment I agreed

**Feature:** F-SGN-002 — Sign | **Status:** pending

---

**Story:** As a signer, I want the exact moment of my signature to be recorded and shown, so that "when did I agree to this" is answered by the product, not by memory.
**Actor:** signer (guest), sender (reads the trail), operator.
**Goal:** `AgreedOn = SignedAtUtc` — captured server-side at the sign action, displayed localized, and carried by the audit entry (F-SGN-003).

## Preconditions

- The signer is about to sign (any of the three methods, US-031-01).

## Happy path

1. **Sign** → the server stamps `SignedAtUtc` (UTC, from the server clock — never the client clock, documented).
2. The success screen: "You've signed {document}." with the localized date and time of the signature.
3. The sender's tracking view shows the signer row with the localized sign time; the audit trail lists the `Signed` entry with its timestamp (FR-032-5).

## Alternative flows

- **Display locale**: the signer sees their locale's date; the sender sees their own. The stored value is the single UTC instant (FR-031-3) — no per-viewer storage.
- **Timezone**: server stamps UTC; the frontend localizes for display only. The final PDF's date stamp uses the signer's locale formatting of that instant (F-SGN-004 burn-in).

## Acceptance criteria

```gherkin
Given I press Sign
When the signature succeeds
Then SignedAtUtc is the server clock at the action, not my browser's clock

Given the same instant
When I see it in my locale and the sender sees it in theirs
Then both are the same moment, formatted differently
```

## Edge cases

- Clock skew: UTC only; no "your time" display of a client-captured value (documented).
- `AgreedOn` is metadata, not a second signature: it travels with the audit entry and the final PDF (FR-031-3, F-SGN-004 FR-033-2).

## UI notes

- Success screen: the date line is `--fs-small`, `--fg-muted` — information, not hero.
- Tracking view: per-signer "Signed {localized datetime}" under the row (F-SGN-003 timeline vocabulary).

## Technical notes

- `Signature.SignedAtUtc DATETIME2 NOT NULL` — set in the same transaction as the row (US-031-01), before `document.signed` is emitted.
- Audit entry `AtUtc` equals `SignedAtUtc` (one instant, two consumers — F-SGN-003 FR-032-2).
- i18n: date formatting strings per F-TRF-014 (no hardcoded date formats).

## Links

- Feature: `SGN-002-sign.md` (FR-031-3, AC-031-1)
- Related: F-SGN-003 (audit carries it), F-SGN-004 (burned into the final), F-TRF-014 (localization)
