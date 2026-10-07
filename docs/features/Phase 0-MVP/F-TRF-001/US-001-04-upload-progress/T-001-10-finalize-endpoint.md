# T-001-10 — FinalizeTransferCommand + endpoint 2: idempotent finalize (T-010)

**Story:** US-001-04 | **Spec:** FR-001-5 (completion), AC-001-1, EC-002-1 (idempotency replay), TA-4.2#2/TA-7.1 | **Size:** M
**Depends on:** T-001-05 (draft endpoint — the draftId it finalizes), T-055-02 (IBlobStore port — server-side blob copy)

---

## Context to read (only these)

- `US-001-04-upload-progress.md` → happy path step 5 + Technical notes
- `../../F-TRF-001-upload-surface.md` → Technical notes (FinalizeTransferCommand line) + AC-001-1
- TA-4.2#2 request/response schema in `docs/03-technical-architecture.md`

## Instructions

1. Add **`FinalizeTransferCommand`** (MediatR, `wa.application/UseCases/Transfers/`) + endpoint 2 `POST /api/v1/transfers/draft/{draftId}/finalize`.
2. Server-side blob copy `staging/{draftId}/f/{fileId} → transfers/{transferId}/files/{fileId}` via `IBlobStore` — only files at 100 % are committed; a draft with zero done files stays un-finalizable.
3. Create **`Transfer(Status=0)`** + one **`FileItem`** per file (names/sizes/contentTypes preserved, duplicate names both kept) + **`BlobRef(RefCount=1)`** — the re-send sharing machinery (F-TRF-010) builds on these rows later.
4. Idempotency: a replayed finalize with the same idempotency key returns the **same transfer** (EC-002-1) — no second `Transfer` row, no duplicate blob copies.
5. Emit **`upload_completed`** telemetry (bytes, durationMs, retries per file and overall — TA-10.2).

## Exit check

- [ ] Finalize of a completed draft returns the TA-4.2#2 `TransferDto` (id, linkId, publicUrl, status "Draft", totalBytes, files)
- [ ] Replaying finalize with the same idempotency key returns the identical transfer — one Transfer row, blobs copied once
- [ ] Blob bytes at `transfers/{transferId}/files/{fileId}` are byte-exact vs the staged blob

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; draft endpoint + IBlobStore port in place).
Task T-001-10 — implement finalize (milestone task T-010).
Read first (only): docs/features/Phase 0-MVP/F-TRF-001/US-001-04-upload-progress/US-001-04-upload-progress.md (happy path step 5 + Technical notes), F-TRF-001-upload-surface.md (Technical notes FinalizeTransferCommand line), and the TA-4.2#2 schema in docs/03-technical-architecture.md.
Do exactly:
1. Add FinalizeTransferCommand (MediatR, wa.application/UseCases/Transfers/) + endpoint 2 POST /api/v1/transfers/draft/{draftId}/finalize.
2. Server-side blob copy staging/{draftId}/f/{fileId} → transfers/{transferId}/files/{fileId} via IBlobStore — only files at 100 % are committed; a draft with zero done files stays un-finalizable.
3. Create Transfer(Status=0) + one FileItem per file (names/sizes/contentTypes preserved, duplicate names both kept) + BlobRef(RefCount=1).
4. Make finalize idempotent: replaying with the same idempotency key returns the same transfer — no second Transfer row, no duplicate blob copies (EC-002-1).
5. Emit upload_completed telemetry with bytes, durationMs, retries per file and overall (TA-10.2).
Done when: T-010's milestone checks hold — TA-4.2#2 response shape, idempotent replay returns the identical transfer, and blob bytes are byte-exact after the copy.
Constraints: thin endpoint (TA-4.2a); FileItem SortOrder = selection order; BlobRef RefCount=1 is what F-TRF-010's re-send increments later — do not add that logic here.
```
