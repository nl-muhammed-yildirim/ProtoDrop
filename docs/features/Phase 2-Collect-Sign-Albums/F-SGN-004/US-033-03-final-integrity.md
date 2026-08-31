# US-033-03 — Trust the final is the real one

**Feature:** F-SGN-004 — Final Document | **Status:** pending

---

**Story:** As a sender (and my counterparty), I want the final document to be verifiably the same document that was signed — source PDF + the signatures, nothing more — so that "which version?" is not a question.
**Actor:** sender, signers, operator.
**Goal:** integrity within MVP honesty: the final is the source PDF with exactly the recorded `Signature` blobs burned in at their recorded positions; the audit trail (F-SGN-003) names who signed what when; the source blob path is stable (`documents/{id}/source.pdf`).

## Preconditions

- A completed document with its `final.pdf` (US-033-01).

## Happy path

1. The merge consumes exactly the `Signature` rows: one burned signature per signed recipient, at its `PageNumber` / placement, with its `AgreedOn` stamp (FR-033-2).
2. The audit trail (US-032-01) shows every `Signed` entry with actor + time — a reader of the trail can recount the signatures in the PDF.
3. Telemetry `document_final_generated { documentId, sizeBytes, durationMs }` records the generation; the blob path is fixed (`documents/{documentId}/final.pdf`) — no moving files.

## Alternative flows

- **A signer declines**: their page has no signature; the trail says `Declined` with the reason — the final reflects exactly the signed set (no ghost signatures).
- **Voided**: no final (EC-033-4) — the trail's `Voided` entry is the whole story.

## Acceptance criteria

```gherkin
Given a completed document with 2 signed recipients
When the final PDF is inspected
Then it contains exactly 2 burned signatures with 2 date stamps, matching the 2 Signed audit entries

Given a voided document
When the sender looks for a final
Then no final.pdf exists and the trail shows Voided
```

## Edge cases

- MVP honesty: "integrity" is *procedural* (same transaction, named blobs, trail recount), not cryptographic — a digest/verification UI is a D-22/Phase 3 decision (documented, so the claim doesn't outrun the mechanism).
- Source vs final: both blobs kept for retention; `source.pdf` is what was sent (F-SGN-001), `final.pdf` is the artifact — the two paths never alias.

## UI notes

- Tracking view after completion lists, per signer: name, signature method (Draw/Type/Upload), date — the human-readable manifest of what's in the PDF.
- No "verified" badge in MVP (the badge would be a crypto claim; the method list is the honest version).

## Technical notes

- Merge input = `SELECT * FROM Signature WHERE DocumentRecipientId IN (…signed…)` — the query IS the manifest; a unit test asserts one burned signature per row (AC above).
- Failure semantics (EC-033-1) keep the sign rows — a re-merge reuses the recorded data, never re-prompts the signer.
- Events: `document.completed` carries `finalBlobPath` — consumers (emails, admin) point at the canonical path.

## Links

- Feature: `SGN-004-final-document.md` (FR-033-2, EC-033-4)
- Related: US-032-01 (the trail this story cross-checks), US-033-01 (the artifact), F-SGN-001 (the source blob)
