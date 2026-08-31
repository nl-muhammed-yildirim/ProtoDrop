# US-030-01 — Send a document for signature

**Feature:** F-SGN-001 — Send Document | **Status:** pending

---

**Story:** As a Pro user, I want to upload a document and send it out for signature, so that people can sign without printing, scanning, or a second product.
**Actor:** Pro/Business account user (plan-gated, D-22).
**Goal:** upload → title → signers (ordered) → **Send**; the first signer is emailed immediately.

## Preconditions

- User on Pro/Business (`PlanContext.Features.sign`); a PDF (or docx) on hand.

## Happy path

1. Document create screen: drop zone (single file), title, signer list (emails, ordered).
2. **Send** → `POST /documents` (endpoint 34) → `document.created`; first signer's email goes out immediately.
3. The sender's tracking link `{origin}/sign/{linkId}` shows each signer's status.

## Alternative flows

- **Free tier**: "Sign requires Pro" (AC-030-5, consistent with F-COL-001/F-ALB-001 gates).
- **Self-sign**: first signer may be the sender's own email (EC-030-1, allowed).

## Acceptance criteria

```gherkin
Given I send a PDF to 2 signers in order
When I press Send
Then signer 1 gets an email immediately
And my tracking link shows both signers as pending

Given I am on the default (Free) plan
When I try to send a document
Then I see "Sign requires Pro"
```

## Edge cases

- docx input → converted to PDF first (US-030-03).
- Large PDF: owner plan `MAX_TRANSFER_SIZE` (EC-030-5).
- Two signers, same email: allowed (they sign in order — EC-030-3, documented).

## UI notes

- Create screen: single-file drop zone, title, signer rows (email + ↑↓ reorder + delete), **Send** primary.
- Helper under signers: "They'll sign in this order."

## Technical notes

- `CreateDocumentCommand` (MediatR, `Documents/` area, TA-4.2a extension).
- `Document` / `DocumentRecipient` DDL (TA-3.2 addition, migration + ADR note).
- Events: `document.created { documentId, linkId, signerCount }` (TA-5.3 extension).

## Links

- Feature: `SGN-001-send-document.md` (FR-030-1, FR-030-5, AC-030-1/5)
- Architecture: TA-3.2, TA-4.2, TA-5.3
- Related: F-SGN-002 (signing), F-SGN-003 (tracking), F-BIL-001 (plan gate)
