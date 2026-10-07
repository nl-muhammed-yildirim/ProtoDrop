# T-001-05 — CreateDraftCommand + endpoint 1: draft creation, per-file cwr SAS (T-009)

**Story:** US-001-03 | **Spec:** FR-001-3 (server half), AC-001-2 (server side), TA-4.2#1 | **Size:** M
**Depends on:** T-056-02 (Problem+JSON pipeline — the 400 body uses it), T-055-03 (BlobSasMinter), T-055-01 (staging path helper)

---

## Context to read (only these)

- `US-001-03-size-validation.md` → happy path step 5 + Technical notes
- `../../F-TRF-001-upload-surface.md` → FR-001-3 + AC-001-2 + Technical notes (endpoint line)
- TA-4.2#1 request/response schema in `docs/03-technical-architecture.md`

## Instructions

1. Add **`CreateDraftCommand`** (MediatR, `wa.application/UseCases/Transfers/`) + endpoint 1 `POST /api/v1/transfers/draft` — metadata only: `{ files: [{name, sizeBytes, contentType}] }`.
2. Server-side limit pre-check against the effective plan's limits via `ILimitsProvider` (TA-3.4): total > `MAX_TRANSFER_SIZE` or any file > `MAX_SINGLE_FILE` → 400 Problem+JSON **`TRANSFER_SIZE_EXCEEDED`** (the client check is UX; this is the rule).
3. Response 201: `{ draftId, expiresInSec: 7200, files: [{fileId, uploadUrl}] }` — each `uploadUrl` is a per-file **`cwr` SAS, 2 h TTL** minted by `BlobSasMinter`, path exactly `/staging/{draftId}/f/{fileId}` (TA-3.5/TA-3.6).
4. Emit the **`upload_started`** telemetry event (endpoint 1's catalog row) with the exact TA-10.2 template via `WaTelemetryReporter`.

## Exit check

- [ ] Draft creation returns per-file SAS URLs; a plain HTTP block upload to one of them succeeds and the blob exists at `/staging/{draftId}/f/{fileId}` (T-009 integration test)
- [ ] An oversized selection returns 400 `TRANSFER_SIZE_EXCEEDED` with no blobs written (AC-001-2 server side)
- [ ] The response shape matches TA-4.2#1 exactly (`draftId`, `expiresInSec`, per-file `fileId` + `uploadUrl`)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; Problem+JSON pipeline, BlobSasMinter, staging path helper in place).
Task T-001-05 — implement draft creation (milestone task T-009).
Read first (only): docs/features/Phase 0-MVP/F-TRF-001/US-001-03-size-validation/US-001-03-size-validation.md (happy path step 5 + Technical notes), F-TRF-001-upload-surface.md (FR-001-3), and the TA-4.2#1 schema in docs/03-technical-architecture.md.
Do exactly:
1. Add CreateDraftCommand (MediatR, wa.application/UseCases/Transfers/) + endpoint 1 POST /api/v1/transfers/draft — metadata only: files with name, sizeBytes, contentType.
2. Server-side limit pre-check via ILimitsProvider (TA-3.4): total > MAX_TRANSFER_SIZE or any file > MAX_SINGLE_FILE → 400 Problem+JSON TRANSFER_SIZE_EXCEEDED (the client check is UX; this is the rule).
3. Respond 201 with { draftId, expiresInSec: 7200, files: [{fileId, uploadUrl}] } — each uploadUrl a per-file cwr SAS with 2 h TTL minted by BlobSasMinter at path /staging/{draftId}/f/{fileId} (TA-3.5/TA-3.6).
4. Emit the upload_started telemetry event via WaTelemetryReporter with the exact TA-10.2 template.
Done when: T-009's integration test holds — draft → SAS upload → blob exists at the canonical staging path, and the oversized case returns 400 TRANSFER_SIZE_EXCEEDED with zero blobs written.
Constraints: thin endpoint (TA-4.2a) — business logic in CreateDraftCommand only; limits read through ILimitsProvider, never literals; no new tables (draft is metadata until finalize).
```
