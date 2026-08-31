# F-SGN-004 — Final Document

**Priority:** P1 (Phase 2b) | **Phase:** 2b — Sign
**Spec source:** `02-feature-plan.md` F-SGN-004 (outline → remapped below as FR-033-*) | **Architecture:** TA-6.6, TA-5.3
**Milestone tasks:** T-051 (M5)

---

## Description

When the last signer signs, the **merged signed PDF is generated async** — the source PDF with each signature placed at its position and an `AgreedOn` date stamp burned in. All parties (sender + all signers) get a download of the **final document** (per-signer email with a 7-day SAS, plus the sender's tracking view). Generation is the same 202-while-working pattern as F-TRF-004 / F-COL-003: request returns 202 with a polling URL, or the email carries the URL directly.

**Actors:** sender (downloads the final), signers (get the final), operator.
**Value:** the artifact everyone actually wanted — the signed PDF, not the process.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-033-1 | On the **last** signature: document → `Completed`; `f-zip`-family function (proposed: `f-sign-final`, or an extension of the zip function with a PDF-merge mode — decision D-22; the *async pattern* is specced) merges signatures into `documents/{id}/final.pdf`. |
| FR-033-2 | Merge: each `Signature.SigBlobRefId` placed at `PageNumber` (default 1), bottom-left (MVP placement, F-SGN-002); date stamp `AgreedOn` next to each signature. |
| FR-033-3 | On success: all parties emailed the final (per-address, F-TRF-006 pipeline, `final_ready` template), sender's tracking view shows **Download final** (30-min SAS, TA-3.6). |
| FR-033-4 | Generation failure: `document_final_failed` telemetry + sender sees "Your final document is being prepared — we'll email it." (retry via the same 202 pattern; 3 attempts → admin alert). |
| FR-033-5 | Final PDF lifetime: kept with the document (deleted at `DOCUMENT_RETENTION_DAYS`, F-SGN-003-6 — same clock as the audit). |
| FR-033-6 | Events (TA-5.3 extension): `document.completed { documentId, finalBlobPath }`, `document.final_failed { documentId, reason }`. |

## Acceptance criteria

```gherkin
AC-033-1: The last signer signs
  Then the document becomes Completed
  And a final.pdf is generated with all signatures and date stamps

AC-033-2: All parties receive the final-document email
  Then each recipient (sender + signers) gets exactly one email with a working download link

AC-033-3: The sender downloads the final from the tracking view
  Then they get the merged PDF (signatures visible, dates present)

AC-033-4: Generation fails
  Then the sender sees the retry state and the final arrives after the retry
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-033-1 | Signature blob missing at merge time | `document.final_failed` with `reason: sig-missing`; admin fix = re-sign (the recipient's sign row is preserved) |
| EC-033-2 | Two signers on the same page, same corner | MVP placement is per-signer page (each signs page 1 bottom-left, offset by index — documented; custom placement is a D-22 decision) |
| EC-033-3 | Final generated, then the sender downloads after retention | 410 + "The final document has expired." (consistent with F-TRF-003 expired) |
| EC-033-4 | Voided document | No final generated (Voided is terminal, F-SGN-001) |

## UI notes (UI-Reference §5)

- Tracking view after completion: green `Completed` chip + **Download final** primary (or "Preparing…" with a spinner state).
- Email: one CTA button "Download the signed document", calm tone, 7-day link (TTL ≥ the email's usefulness; consistent with F-TRF-003 SAS TTL 30 min for the *first* hit, then the email link is the SAS — use the 7-day SAS variant, TA-3.6 addition for final documents, ADR note).

## Technical notes

- Function: proposed `f-sign-final` (timer/HTTP, like `f-zip`, TA-6.6 pattern — 202 + polling or direct URL). D-22 decides the function shape; the pattern is frozen.
- PDF merge library: decision D-22 (proposed: `PDFSharp` or `iText` — both need an ADR line per TA-17 golden rule).
- SAS for the final: 7-day read (TA-3.6 addition, ADR note) — justified: emails must outlive the 30-min page SAS.
- Events per FR-033-6; telemetry `document_final_generated { documentId, sizeBytes, durationMs }`.

## Test plan

- Unit: placement math (page, corner, offset by index); date-stamp format.
- Integration: AC-033-1…033-4 (fake merge: assert final.pdf exists with N signature refs; all parties emailed exactly once; failure → retry → success).
- E2E: 2-signer document fully signed → both signers + sender receive the final email; download renders the merged PDF.

## User stories

| ID | Story | File |
|---|---|---|
| US-033-01 | Get the signed document | `US-033-01-final-pdf.md` |
| US-033-02 | Get the final document without asking | `US-033-02-final-notified.md` |
| US-033-03 | Trust the final is the real one | `US-033-03-final-integrity.md` |
