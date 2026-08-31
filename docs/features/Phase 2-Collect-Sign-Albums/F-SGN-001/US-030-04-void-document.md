# US-030-04 — Void a document in flight

**Feature:** F-SGN-001 — Send Document | **Status:** pending

---

**Story:** As a sender, I want to void a document while signers are still pending, so that a changed deal stops being signed by people who already got their links.
**Actor:** sender, pending signers.
**Goal:** **Void** → `Status = Voided`; pending signers' links show "This document was voided by {sender}."; no further emails.

## Preconditions

- A document in `Sending` with ≥ 1 pending signer.

## Happy path

1. **Void** on the tracking view (confirm modal: "Void this document? Pending signers will be notified.").
2. `Status = Voided`; `document.voided` event; audit entry (F-SGN-003, actor = sender).
3. Pending signers' links flip to the voided state; their emails stop.

## Alternative flows

- **Already completed**: not voidable — "This document is already completed." toast (EC-030-4).
- **Void then re-send**: a new document (void is terminal for that document, documented).

## Acceptance criteria

```gherkin
Given a document with signer 2 pending
When I void it
Then signer 2's link shows the voided state
And no further signer emails are sent
```

## Edge cases

- Voided state is terminal (no un-void; re-send is the flow).
- The voided audit entry names the sender (F-SGN-003, AC-032-4).

## UI notes

- **Void** ghost button on the tracking view, visible while any signer is pending; confirm modal.
- Voided state screen (signer): 4.6-style, "This document was voided by {sender}." + no action (or "Refresh" ghost).

## Technical notes

- `POST /documents/{id}/void` (endpoint 36); `VoidDocumentCommand`.
- Chain stop: the advance function skips `Voided` documents (F-SGN-002 token state machine).
- Events: `document.voided { documentId }` (TA-5.3 extension).

## Links

- Feature: `SGN-001-send-document.md` (FR-030-7, AC-030-4)
- Related: F-SGN-002 (signer states), F-SGN-003 (audit)
