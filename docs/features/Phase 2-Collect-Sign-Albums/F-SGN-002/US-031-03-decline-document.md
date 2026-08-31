# US-031-03 — Decline when the document isn't right

**Feature:** F-SGN-002 — Sign | **Status:** pending

---

**Story:** As a signer, I want to decline a document instead of signing, so that a wrong version or a changed mind doesn't get papered over with a signature I don't stand behind.
**Actor:** signer (guest), sender (sees the reason).
**Goal:** a **Decline** action with an optional reason (≤ 200 chars) — terminal for that document in MVP (documented, FR-031-6).

## Preconditions

- It is the signer's turn; the document is `Sending`.

## Happy path

1. **Decline** ghost button → small dialog: reason field (optional, ≤ 200 chars, counter visible) + **Confirm decline**.
2. `POST /api/v1/sign/{linkId}/sign/{token}/decline` (endpoint 38) → recipient `Status = Declined`, reason stored, `Declined` audit entry written.
3. The sender's tracking view shows the Declined chip and the reason; the document can no longer complete (US-033-01).

## Alternative flows

- **No reason**: allowed — the chip says `Declined`, the reason is "— ".
- **Sender visibility**: the tracking view shows it immediately (no email needed for MVP — the sender is watching the document, documented; a sender-notification email is a Phase 3 nicety).

## Acceptance criteria

```gherkin
Given it is my turn
When I decline with a reason
Then the recipient is Declined and the sender's tracking shows the reason

Given I decline without a reason
When the sender views the document
Then the Declined chip is shown and the reason is empty
```

## Edge cases

- Decline is terminal in MVP (FR-031-6): no "un-decline" — the sender's remedy is to resend a new document (documented; reversible-by-re-invite is a D-22 note).
- Declined after a partial draw: the pad content is discarded (the reason field is the record, documented).

## UI notes

- **Decline** is ghost-styled next to **Sign** — deliberate secondary weight (signing is the expected path).
- Dialog: single reason field + counter + **Confirm decline** (not "delete" — decline is kinder than delete).

## Technical notes

- `DocumentRecipient.Status = Declined`; the decline reason (≤ 200 chars) is stored on the recipient row (the `Signature` table is for actual signatures — documented).
- `document.signed` does NOT fire on decline; the `Declined` audit entry (F-SGN-003) is the record; telemetry `document_declined`.

## Links

- Feature: `SGN-002-sign.md` (FR-031-6, AC-031-5, EC-031-5)
- Related: US-031-01 (the button lives on that screen), F-SGN-003 (audit), US-033-01 (completion blocked)
