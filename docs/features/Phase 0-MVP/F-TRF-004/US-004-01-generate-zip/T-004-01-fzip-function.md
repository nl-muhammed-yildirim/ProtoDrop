# T-004-01 — f-zip function: streaming ZipArchive, de-dup, cap check (TA-6.6)

**Story:** US-004-01 | **Spec:** FR-004-2/5, AC-004-3, TA-6.6 | **Size:** M
**Depends on:** T-001-10 (finalize — files exist at `transfers/{id}/files/{fileId}`)

---

## Context to read (only these)

- `US-004-01-generate-zip.md` → happy path + Edge cases + Technical notes
- `../../F-TRF-004-download-zip.md` → FR-004-2/5 + AC-004-3 + EC-004-1/2 + Test plan (unit line)

## Instructions

1. Add the **`f-zip`** Azure Function (`wa.workers`, HTTP trigger, 4-way concurrency, 60-min timeout): `POST /zip/{transferId}` behind Front Door with the **`x-zip-sign`** shared-secret header (TA-9.3).
2. Generation (TA-6.6): if `transfers/{id}/all.zip` exists → return its URL; else stream files (ordered by `SortOrder`) through a **`ZipArchive` in stream mode** into the blob (`maxBufferSize=4MB`, `leaveOpen=true`); entry names = `OriginalName` with deterministic `_1`/`_2` de-dup on collision (FR-004-5).
3. Cap check **before** starting: `SUM(FileItem.SizeBytes) > MAX_ZIP_SIZE` → 413 (TA-6.6 step 5; double safety — the UI should make this unreachable, US-004-03).
4. On success emit **`zip.generated`** (`{ transferId, sizeBytes, durationMs }`); on failure delete the partial zip + 500 + **`zip_failed`** telemetry (EC-004-2: expiry mid-generation leaves no partial blob).

## Exit check

- [ ] Unit: entry-name de-dup logic — two "report.pdf" → `report.pdf` + `report_1.pdf`, contents intact
- [ ] 4 GB zip streams without buffering the whole archive (memory guard / streaming assertion)
- [ ] Failure mid-generation deletes the partial blob; next request regenerates cleanly

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.workers project, blob layout transfers/{id}/files/{fileId}).
Task T-004-01 — implement the f-zip function.
Read first (only): docs/features/Phase 0-MVP/F-TRF-004/US-004-01-generate-zip/US-004-01-generate-zip.md (happy path + Edge cases + Technical notes) and F-TRF-004-download-zip.md (FR-004-2/5).
Do exactly:
1. Add the f-zip Azure Function (wa.workers, HTTP trigger, 4-way concurrency, 60-min timeout): POST /zip/{transferId} behind Front Door with the x-zip-sign shared-secret header (TA-9.3).
2. Generation per TA-6.6: if transfers/{id}/all.zip exists → return its URL; else stream files (ordered by SortOrder) through a ZipArchive in stream mode into the blob (maxBufferSize=4MB, leaveOpen=true); entry names = OriginalName with deterministic _1/_2 de-dup on collision (FR-004-5).
3. Cap check before starting: SUM(FileItem.SizeBytes) > MAX_ZIP_SIZE → 413 (TA-6.6 step 5; double safety — the UI should make this unreachable, US-004-03).
4. On success emit zip.generated ({ transferId, sizeBytes, durationMs }); on failure delete the partial zip + 500 + zip_failed telemetry (EC-004-2: expiry mid-generation leaves no partial blob).
Done when: the unit tests hold — de-dup produces report.pdf + report_1.pdf with intact contents, streaming never buffers the whole archive, and a failed generation deletes its partial blob.
Constraints: ZipArchive stream mode is mandatory (TA-6.6) — do not build the zip in memory; MAX_ZIP_SIZE comes from ILimitsProvider (never a literal); the function is idempotent by path (T-004-02 builds on that).
```
