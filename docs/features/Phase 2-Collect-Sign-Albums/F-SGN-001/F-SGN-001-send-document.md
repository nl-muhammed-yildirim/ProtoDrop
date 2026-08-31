# F-SGN-001 — Send Document

**Priority:** P1 (Phase 2b) | **Phase:** 2b — Sign
**Spec source:** `02-feature-plan.md` F-SGN-001 (outline → remapped below as FR-030-*) | **Architecture:** TA-3.2, TA-4.2, TA-5.3
**Milestone tasks:** T-047, T-048 (M5)

---

## Description

Sign is **e-sign at the intersection of transfer and signature**: the sender uploads a document (PDF, or docx converted to PDF), assigns **signers in an explicit order**, and each signer gets an email with a link to sign. A document is a separate entity (`Document`) — not a Transfer, not a Collection: it has **recipients with roles and sequence**, and it produces an **audit trail** and a **final merged PDF**.

**Actors:** sender (account user, plan-gated), signer (guest), operator.
**Value:** the "documents" product line — contract-ish flows (NDA, invoice ack, onboarding) without a heavyweight e-sign vendor.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-030-1 | Send a document: upload **PDF** (or **docx → PDF** server-side conversion), title, per-recipient **sign order** (1, 2, 3… — sequential by default; parallel is a later decision, D-22 proposed), each recipient by email. |
| FR-030-2 | `Document` entity (TA-3.2 addition, migration): `Id, OwnerAppUserId, Title, SourceBlobRefId, PdfBlobRefId?, Status (Draft|Sending|Signed|Completed|Voided), SequenceIndex?, CreatedAtUtc`. `DocumentRecipient`: `Id, DocumentId, Address, Order, Status (Pending|Signed|Declined), SignedAtUtc?`. |
| FR-030-3 | Plan-gated: `PlanContext.Features.sign` (D-22 proposed: Pro + Business). Free sees "Sign requires Pro" (consistent with F-COL-001 pattern). |
| FR-030-4 | docx → PDF conversion: server-side (proposed: Aspose or LibreOffice headless in a Function — decision D-22; the *choice* is the open item, the *conversion exists* is specced). Failure → `DOCX_CONVERT_FAILED` Problem+JSON, document stays Draft. |
| FR-030-5 | Sending: first signer's email goes out immediately; subsequent signers get theirs **when the previous signs** (sequential chain, F-SGN-002). Document stays `Sending` until all sign. |
| FR-030-6 | Sender link: `{origin}/sign/{linkId}` — the document's own 8-char link (same generator), for the sender's tracking view (status of each signer, F-SGN-003 audit). |
| FR-030-7 | Void: sender can void a document while any signer is Pending (status `Voided`); pending signers' links show "This document was voided by {sender}." |

## Acceptance criteria

```gherkin
AC-030-1: A Pro user uploads a PDF and sends to 2 signers in order
  Then signer 1 gets an email immediately
  And signer 2 gets an email only after signer 1 signs

AC-030-2: A docx upload is converted
  Then the stored document is a PDF (viewable/audited), title preserved

AC-030-3: The sender opens {origin}/sign/{linkId}
  Then they see each signer with their status (pending / signed / declined)

AC-030-4: The sender voids a document while signer 2 is pending
  Then signer 2's link shows the voided state and no further emails go out

AC-030-5: A Free user (default plan) tries to send a document
  Then they see "Sign requires Pro"
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-030-1 | Signer email = sender email | First signer may be the sender (self-sign) — allowed, documented |
| EC-030-2 | docx conversion fails (corrupt file) | `DOCX_CONVERT_FAILED`, file listed as failed in the create screen, retryable |
| EC-030-3 | Two signers with the same email | Allowed (they sign twice — rare, documented; Phase 3 dedup decision) |
| EC-030-4 | Document voided after all signed | Not voidable — it's `Completed` (terminal); sender sees a toast |
| EC-030-5 | Very large PDF (> plan limit) | Owner's plan `MAX_TRANSFER_SIZE` applies (same rule as F-COL-002) |

## UI notes (UI-Reference §5.4/§5.5 extension)

- Create screen: document drop zone (single file), title, ordered signer list (email rows with ↑↓ reorder, delete), **Send**. Helper under signers: "They'll sign in this order."
- Sender tracking view (`/sign/{linkId}`): document title, signer rows (name/email, status chip, signed time), **Void** ghost button (when any pending).
- Tone: "Send a document for signature." — calm.

## Technical notes

- Endpoints (TA-4.2 extension, ADR note): `POST /api/v1/documents` (34), `GET /api/v1/documents/{id}` (35), `POST /api/v1/documents/{id}/void` (36).
- MediatR: `CreateDocumentCommand`, `GetDocumentQuery`, `VoidDocumentCommand`.
- Blob layout (TA-3.5 addition): `documents/{documentId}/source.pdf`, `documents/{documentId}/final.pdf`.
- Events (TA-5.3 extension): `document.created { documentId, linkId, signerCount }`, `document.voided { documentId }`.
- Telemetry (TA-10.2 extension): `document_sent`, `document_voided`.
- Conversion: Function `f-convert` is **not** part of TA-6.2 inventory in MVP-2 — decision D-22 (inline in API for MVP-2b, Function later if p99 is bad). Marked "proposed" here.

## Test plan

- Unit: order normalization (no gaps, ≥ 1 signer); status enum; void-transition rules.
- Integration: AC-030-1 (email sequence with fake clock + fake sender), AC-030-4 (void stops pending emails), AC-030-5; docx→PDF with a sample file (conversion fake in unit).
- E2E: upload PDF → send → signer 1 signs → signer 2 email (Playwright + seeded signers).

## User stories

| ID | Story | File |
|---|---|---|
| US-030-01 | Send a document for signature | `US-030-01-send-document.md` |
| US-030-02 | Choose who signs in what order | `US-030-02-signer-order.md` |
| US-030-03 | Send a Word file (it becomes a PDF) | `US-030-03-docx-to-pdf.md` |
| US-030-04 | Void a document in flight | `US-030-04-void-document.md` |
