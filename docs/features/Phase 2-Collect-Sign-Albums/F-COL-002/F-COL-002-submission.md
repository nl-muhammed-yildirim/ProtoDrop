# F-COL-002 — Submission

**Priority:** P1 (Phase 2a) | **Phase:** 2a — Collect
**Spec source:** `02-feature-plan.md` F-COL-002 (outline → remapped below as FR-026-*) | **Architecture:** TA-8.3, TA-3.5, TA-4.2, TA-7.1
**Milestone tasks:** T-043 (M5)

---

## Description

The contributor side: a **guest with no account** opens the collection link, fills the fields the collector required (name / email), uploads files through the **same chunked upload engine** as the landing page (F-TRF-001 — client reuse, per-block SAS to staging), and gets a confirmation. **Multiple submissions per person are allowed**; each submission is a separate `CollectionEntry` with its **own status** (F-COL-003/005). The collector is notified per submission (email, F-COL-005 pipeline).

**Actors:** contributor (guest), collector (notified), operator.
**Value:** the "many" in "many → one" — zero-friction for the people *sending*.

## Functional requirements

| ID | Requirement |
|---|---|
| FR-026-1 | Contributor (no account) uploads to a collection via the collection link. Fields rendered per the collection's field setup (F-COL-001-1): name (if required), email (if required), files (if required). |
| FR-026-2 | Upload is chunked through the shared `UploadEngine` (TA-8.3): `POST /api/v1/public/collections/{linkId}/submissions/draft` (endpoint 28) → per-file `cwr` SAS (TA-3.6, 2 h) → `POST …/submissions/draft/{draftId}/finalize` (endpoint 29) creates the `CollectionEntry` + `FileItem`s (BlobRef shared). |
| FR-026-3 | **Multiple submissions per person allowed** — no uniqueness constraint on (collection, person); each submission = new `CollectionEntry` row with its own status, its own files, its own size. |
| FR-026-4 | Per-submission limit = **owner's plan** `MAX_TRANSFER_SIZE` (the collector's plan pays for the storage); validated at draft (server-side) and pre-checked client-side (F-TRF-007 semantics). |
| FR-026-5 | Confirmation screen: "Your files are in." + what happens next ("{CollectorName} has been notified."). If the contributor gave an email and the collector enabled it: a confirmation email (F-COL-005 pipeline). |
| FR-026-6 | `CollectionEntry` (TA-3.2 addition, migration): `Id, CollectionId, SenderName?, SenderEmail?, SizeBytes, FileCount, Status (Received|Accepted|Declined|Done), CreatedAtUtc, DoneAtUtc?`. `FileItem` generalized: nullable `CollectionEntryId` (XOR with `TransferId`) — migration + ADR note. |
| FR-026-7 | Staging lifecycle: same 24 h rule (TA-3.5) — abandoned drafts of collection submissions die the same way. |

## Acceptance criteria

```gherkin
AC-026-1: A guest submits 2 files to an open collection
  Then the submission succeeds, a CollectionEntry with Status=Received is created
  And the files are visible in the collector's dashboard

AC-026-2: The same person submits twice
  Then two separate entries exist, each with its own status and files

AC-026-3: A submission exceeds the owner's plan limit
  Then the draft is rejected with the limit named (before upload starts, client pre-check)

AC-026-4: A contributor who gave an email receives the confirmation email
  Then it names the collection and the submitter's files

AC-026-5: A collection that is PastDue still accepts a submission
  Then the entry is created (within the grace window, F-COL-004)
```

## Edge cases

| ID | Case | Behavior |
|---|---|---|
| EC-026-1 | Contributor gives no email when only "optional" | Allowed; no confirmation email (F-COL-005) |
| EC-026-2 | Collection closes mid-upload | Finalize race: if `Status=Closed` at finalize → entry is created then immediately marked Declined with `reason: closed` (collector can re-open, F-COL-004) — documented |
| EC-026-3 | Duplicate file names across submissions | Kept per entry; zip de-dup per F-TRF-004 on per-person download |
| EC-026-4 | Contributor abandons after draft | 24 h staging lifecycle (TA-3.5), no entry row created |
| EC-026-5 | Email field with an invalid address | Required → inline validation; optional → normalized-or-empty |

## UI notes (UI-Reference §5.1/§5.3 blend)

- Contributor page: header "Files for {title}" + description, field block (name/email per setup), drop zone (4.1) + file rows (4.2) + overall progress (4.2) — **identical upload UX to the landing page**.
- Confirmation screen: single card, "Your files are in." + next step; "Send something else" ghost button (→ same collection) and "Done" (→ `/`).
- No marketing, no "powered by" (product rule, 01-product-analysis §10).

## Technical notes

- Endpoints 28/29 (TA-4.2 extension, ADR note); MediatR `CreateSubmissionDraftCommand`, `FinalizeSubmissionCommand`.
- `FileItem` generalization (migration + ADR note):
  ```sql
  ALTER TABLE dbo.FileItem ADD CollectionEntryId UNIQUEIDENTIFIER NULL;
  -- CHECK (TransferId IS NULL) <> (CollectionEntryId IS NULL)
  ```
- `BlobRef` shared exactly as with transfers (RefCount=1 on create; re-send/collect don't duplicate bytes).
- Blob layout: `collections/{collectionId}/entries/{entryId}/files/{fileId}` (TA-3.5 addition) — `transfers/` stays transfer-only.
- Events (TA-5.3 extension): `collection.entry.created { collectionId, entryId, senderEmail?, sizeBytes, fileCount }`.
- Telemetry (TA-10.2 extension): `collection_submitted { collectionId, sizeBytes, durationMs, retries }`.
- Notification to collector: via `collection.entry.created` → `f-email` (owner notification template), deduped like F-TRF-006.

## Test plan

- Unit: field-setup rendering logic; size limit math (owner plan); FileItem XOR validation.
- Integration: AC-026-1…026-3 (draft → SAS → finalize → entry row; double submission → two rows; over-limit draft rejected); AC-026-5 (PastDue collection accepts).
- E2E: guest submits 2 files (Playwright) → dashboard shows the entry; confirmation screen renders.

## User stories

| ID | Story | File |
|---|---|---|
| US-026-01 | Send my files to a collection without an account | `US-026-01-submit-as-guest.md` |
| US-026-02 | Fill in only what's required | `US-026-02-fill-required-fields.md` |
| US-026-03 | Submit again when I have more files | `US-026-03-submit-multiple-times.md` |
