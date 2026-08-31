# US-026-01 — Send my files to a collection without an account

**Feature:** F-COL-002 — Submission | **Status:** pending

---

**Story:** As a contributor with no account, I want to open the collection link and upload my files, so that sending files to a collection takes as little effort as sending them to one person.
**Actor:** contributor (guest), collector (receives).
**Goal:** the same chunked upload UX as the landing page (F-TRF-001), pointed at a collection.

## Preconditions

- An open (or PastDue-in-grace) collection; guest opens `/collect/{linkId}`.

## Happy path

1. Contributor page: "Files for {title}" + description + required fields.
2. Drop files (or choose, F-TRF-017-1) → staging list → upload (UploadEngine, TA-8.3) → `draft` → SAS → `finalize` → `CollectionEntry` (Status=Received).
3. Confirmation: "Your files are in." + "{CollectorName} has been notified."

## Alternative flows

- **No account ever**: the whole flow is guest (FR-026-1).
- **Files off (metadata-only collection)**: just the fields, no upload (F-COL-001 field setup).

## Acceptance criteria

```gherkin
Given I am a guest with files staged
When I submit to an open collection
Then a CollectionEntry with Status=Received is created
And the collector's dashboard shows my files

Given I see the confirmation screen
When I read it
Then it says my files are in and the collector has been notified
```

## Edge cases

- Abandoned draft → 24 h staging lifecycle (TA-3.5, EC-026-4).
- Limit = owner's plan `MAX_TRANSFER_SIZE` (FR-026-4, pre-check client-side, server at draft).

## UI notes

- Contributor page = landing-page upload UX (drop zone 4.1, file rows 4.2, progress) + a fields card on top.
- Confirmation screen: single card, "Your files are in." + next step + **Send something else** (same collection) / **Done** (→ `/`).

## Technical notes

- Endpoints 28/29 (TA-4.2 extension): draft + finalize, per-file `cwr` SAS (TA-3.6, 2 h).
- `collection.entry.created` (TA-5.3 extension) → collector notification (F-COL-005 pipeline).
- Blob layout `collections/{cid}/entries/{eid}/files/{fileId}` (TA-3.5 addition).

## Links

- Feature: `COL-002-submission.md` (FR-026-1, FR-026-2, FR-026-5, AC-026-1)
- Architecture: TA-8.3, TA-3.6, TA-3.5
- Related: F-TRF-001 (upload engine), F-COL-003 (dashboard)
