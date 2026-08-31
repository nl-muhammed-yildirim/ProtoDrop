# F-SGN-002 — Sign

**Priority:** P1 (Phase 2b) | **Phase:** 2b — Sign
**Spec source:** `02-feature-plan.md` F-SGN-002 (outline → remapped below as FR-031-*) | **Architecture:** TA-3.2, TA-4.2, TA-9.4
**Milestone tasks:** T-049 (M5)

---

## Description

The signer's moment: open the link, **draw, type, or upload** a signature, and the document is **date-stamped** and recorded with `AgreedOn` metadata. A signer is a guest — no account, no "create password". The signature is a PNG (drawn/typed/uploaded) that is **burned into the final PDF** at completion (F-SGN-004); in the UI the signer sees their signature placed on the page. The audit entry (F-SGN-003) is written atomically with the sign action.

**Actors:** signer (guest), sender (sees the progress), operator.
**Value:** the lowest-friction signature UX — three input modes, one tap to finish.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-031-1 | Signer page (`/sign/{linkId}/sign/{token}`): the document's pages (rendered PDF), a signature pad with three modes — **draw** (canvas), **type** (styled font → rendered to the same PNG), **upload** (image ≤ 1 MB, PNG/JPEG/WebP) — and a **Sign** button. |
| FR-031-2 | On sign: `Signature` row (TA-3.2 addition, migration): `Id, DocumentRecipientId, Method (Draw|Type|Upload), SigBlobRefId, PageNumber, X?, Y? (optional placement; default bottom-left of page 1 — no drag in MVP), SignedAtUtc (AgreedOn)`. |
| FR-031-3 | **Date stamp**: `AgreedOn = SignedAtUtc` (UTC, displayed localized). The audit entry (F-SGN-003) carries it. |
| FR-031-4 | Signer auth: one-time token in the sign URL (600 s TTL not needed — token valid while the recipient is Pending; single-use on sign). Wrong state (already signed / voided / not their turn) → exact-state screens. |
| FR-031-5 | After signing: "You've signed {document}." + "Next: {next signer} will be notified." (no next → "The sender will be notified."). |
| FR-031-6 | Decline: signer can **decline** instead (reversible by the sender re-inviting? MVP: decline is terminal for that document, documented) with a reason (optional, ≤ 200 chars). |
| FR-031-7 | PII: signer name (derived from email local part if not given; optional "Your name" field) is PII (TA-9.4) — hashed in telemetry. |

## Acceptance criteria

```gherkin
AC-031-1: A signer draws a signature and signs
  Then a Signature row exists with Method=Draw and AgreedOn set
  And an audit entry (F-SGN-003) is written

AC-031-2: A signer types a name and signs
  Then Method=Type and the rendered PNG is stored

AC-031-3: A signer opens a link that isn't their turn
  Then they see "It's not your turn yet — {previous signer} signs first."

AC-031-4: The same signer signs twice (double-click / refresh)
  Then exactly one Signature row exists (single-use token)

AC-031-5: A signer declines with a reason
  Then the recipient is Declined, the sender's tracking shows the reason
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-031-1 | Token URL reused after signing | "Already signed" screen (idempotent, no error) |
| EC-031-2 | Signer's browser without canvas (ancient) | Type/upload modes still work (draw is the default, not required) |
| EC-031-3 | Uploaded signature is 2 MB | Rejected with `SIGNATURE_TOO_LARGE` (1 MB cap, TA-4.1.3) |
| EC-031-4 | Signer with a keyboard-only browser | Type mode + keyboard submit (F-TRF-016 applies) |
| EC-031-5 | Document voided between load and sign | Sign → 409 state screen "This document was voided." (re-fetch status) |

## UI notes (UI-Reference §4, §5)

- Signer page: PDF pages (read-only, `--bg-subtle` gutter), signature pad card (three tabs: Draw / Type / Upload), **Sign** primary + **Decline** ghost.
- Signature pad: 300×150 canvas (draw), font select for Type (one script face, no external font — system `cursive` stack; documented), upload preview.
- State screens: not-your-turn / already-signed / voided / declined — 4.6-style single screen, `Ref: {id8}` where applicable.

## Technical notes

- Endpoints (TA-4.2 extension, ADR note): `POST /api/v1/sign/{linkId}/sign/{token}/signature` (37, uploads the sig image → 201 + state), `POST /api/v1/sign/{linkId}/sign/{token}/decline` (38).
- `Signature` DDL (TA-3.2 addition, migration + ADR note):
  ```sql
  CREATE TABLE dbo.Signature (
      Id                   UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_Signature PRIMARY KEY,
      DocumentRecipientId  UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Sig_Recip REFERENCES dbo.DocumentRecipient(Id),
      Method               TINYINT NOT NULL,  -- 0 Draw, 1 Type, 2 Upload
      SigBlobRefId         UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_Sig_Blob REFERENCES dbo.BlobRef(Id),
      PageNumber           INT NOT NULL CONSTRAINT DF_Sig_Page DEFAULT 1,
      SignedAtUtc          DATETIME2 NOT NULL,
      DeclineReason        NVARCHAR(200) NULL
  );
  ```
- Blob layout (TA-03.5 addition): `documents/{documentId}/signatures/{recipientId}.png`.
- Token: `AuthToken` table (purpose 3 = sign) or a new column-free token in the sign URL (HMAC, single-use — decision D-22; proposed: `AuthToken` with a new `Purpose` value, no schema change beyond the TINYINT).
- Events (TA-5.3 extension): `document.signed { documentId, recipientId }` (drives F-SGN-003 audit + F-SGN-004 final PDF + next-signer email).
- Telemetry (TA-10.2 extension): `signature_created`.

## Test plan

- Unit: token state machine (pending/signed/voided/not-your-turn); method enum; size/type validation.
- Integration: AC-031-1…031-5 (one Signature row per recipient, replay-safe; decline path); audit row atomic with signature (same transaction).
- E2E: full sign flow in browser (Playwright): draw → sign → next signer's email.

## User stories

| ID | Story | File |
|---|---|---|
| US-031-01 | Sign by drawing, typing, or uploading | `US-031-01-sign-with-3-methods.md` |
| US-031-02 | Be sure of the moment I agreed | `US-031-02-agreed-on.md` |
| US-031-03 | Decline when the document isn't right | `US-031-03-decline-document.md` |
| US-031-04 | Know whose turn it is | `US-031-04-turn-state.md` |
