# US-033-01 — Get the signed document

**Feature:** F-SGN-004 — Final Document | **Status:** pending

---

**Story:** As a sender, I want the merged signed PDF — every signature and its date stamp burned in — so that the artifact I file away is the document itself, not a link.
**Actor:** sender (downloads), signers (get it too), operator.
**Goal:** when the **last** signer signs, the document completes and `final.pdf` is generated async with all signatures and `AgreedOn` stamps (FR-033-1/2).

## Preconditions

- A document `Sending` with ≥ 1 signer; all but the last have signed.

## Happy path

1. Last **Sign** → `document` → `Completed` → the final-PDF job starts (`f-sign-final` or the zip function's PDF-merge mode — D-22; the 202 pattern is specced).
2. Merge: each `Signature.SigBlobRefId` placed at its `PageNumber` (default 1), bottom-left, offset by signer index (EC-033-2); the `AgreedOn` date stamp rendered next to each signature.
3. Output: `documents/{documentId}/final.pdf`; the sender's tracking view shows **Download final** (30-minute SAS, TA-3.6) — or "Preparing…" while the job runs (FR-033-3, AC-033-3).

## Alternative flows

- **Generation in flight when the sender opens the view**: "Your final document is being prepared — we'll email it." (the retry state, FR-033-4) — no dead button.
- **Voided document**: no final is generated — `Voided` is terminal (EC-033-4, US-030-04).
- **Download after retention**: 410 + "The final document has expired." — consistent with F-TRF-003 expired semantics (EC-033-3).

## Acceptance criteria

```gherkin
Given the last signer signs
When the final job completes
Then the document is Completed and final.pdf exists with all signatures and date stamps

Given the sender opens the tracking view after completion
When they press Download final
Then the merged PDF downloads (signatures visible, dates present)
```

## Edge cases

- Signature blob missing at merge (EC-033-1): `document.final_failed { reason: sig-missing }`; admin fix = re-sign (the recipient's sign row is preserved — no data loss).
- Two signers, same page: each signs page 1 bottom-left, offset by index — documented placement, custom placement is D-22.

## UI notes

- Tracking view after completion: green `Completed` chip + **Download final** primary; the "Preparing…" state renders on the same button (spinner, not a new element).
- One button, one label in every state: Preparing… / Download final / Expired.

## Technical notes

- Function: `f-sign-final` (proposed, TA-6.6 pattern like `f-zip` — 202 + polling or direct URL); PDF library decision D-22 (PDFSharp or iText — ADR line per TA-17 golden rule).
- Events: `document.completed { documentId, finalBlobPath }`; `document_final_generated { documentId, sizeBytes, durationMs }` telemetry.
- Retention: final PDF deleted at `DOCUMENT_RETENTION_DAYS` (FR-033-5 — same clock as the audit, F-SGN-003 FR-032-6).

## Links

- Feature: `SGN-004-final-document.md` (FR-033-1/2/3/5, AC-033-1/3, EC-033-1/2/3/4)
- Related: US-031-02 (the stamps that get burned in), US-033-02 (the emails), US-033-03 (integrity)
