# US-030-03 — Send a Word file (it becomes a PDF)

**Feature:** F-SGN-001 — Send Document | **Status:** pending

---

**Story:** As a sender, I want to upload a .docx and have it stored as a PDF, so that the signed document is the same artifact everyone gets — not a live Word file someone can re-flow.
**Actor:** sender.
**Goal:** docx upload → server-side conversion → `PdfBlobRefId`; the title is preserved; failure is a named error.

## Preconditions

- Document create screen; a .docx selected.

## Happy path

1. Upload .docx → conversion runs (proposed: inline in the API for MVP-2b, D-22).
2. Stored as `documents/{id}/source.pdf` (or `…/converted.pdf`), `PdfBlobRefId` set.
3. The document is now a PDF everywhere (signing, audit, final).

## Alternative flows

- **PDF upload**: no conversion (stored as the source).
- **Conversion fails** (corrupt file): `DOCX_CONVERT_FAILED`, the file is listed as failed on the create screen, retryable (EC-030-2).

## Acceptance criteria

```gherkin
Given I upload a .docx
When the document is created
Then the stored artifact is a PDF with the title preserved

Given a corrupt .docx
When conversion runs
Then the error is DOCX_CONVERT_FAILED and the document stays Draft
```

## Edge cases

- Non-PDF/docx types rejected at selection (client) and at the API (`UNSUPPORTED_DOCUMENT_TYPE`).
- The conversion choice (library/function) is D-22; the *behavior* (docx becomes a PDF) is specced.

## UI notes

- Drop zone accepts `application/pdf`, `application/vnd.openxmlformats-officedocument.wordprocessingml.document`.
- Failed file row: `--danger` name + "Try again" (the F-TRF-013 pattern, one line).

## Technical notes

- Conversion: D-22 decision (proposed inline in API for MVP-2b, Function later if p99 is bad); `f-convert` not yet in the TA-6.2 inventory.
- Blob: `documents/{documentId}/source.pdf` (TA-3.5 addition).
- Telemetry `document_convert_failed { reason }` (PII-safe).

## Links

- Feature: `SGN-001-send-document.md` (FR-030-4, AC-030-2)
- Architecture: TA-3.5, TA-4.1.3
- Decisions: D-22
