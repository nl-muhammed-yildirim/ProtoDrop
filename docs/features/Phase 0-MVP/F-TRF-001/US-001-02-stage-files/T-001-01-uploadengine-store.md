# T-001-01 — UploadEngine Zustand store (TA-8.3 contract)

**Story:** US-001-02 | **Spec:** FR-001-2, AC-001-3 (state shape), TA-8.3 | **Size:** M
**Depends on:** T-049-10 (wa.web in the solution — landing stub exists)

---

## Context to read (only these)

- `US-001-02-stage-files.md` → happy path + Alternative flows + Technical notes
- `../../F-TRF-001-upload-surface.md` → FR-001-2 + EC-001-2/3 + Technical notes (TA-8.3 line)

## Instructions

1. Add **`src/wa.web/src/core/upload/UploadEngine.ts`** — a Zustand store implementing the TA-8.3 contract exactly:
   - State: `{ files: [{id, name, size, status: queued|uploading|done|failed, progress}], overall: {sentBytes, totalBytes} }`.
   - API: `start(draft)`, `retry(fileId)`, `remove(fileId)`, `reset()`.
2. At this task only the **staging half** is real: files are added/removed while `status` stays `queued`; `start`/`retry` are stubs that flip status (the block-upload loop arrives in T-001-07).
3. File order = selection order (`SortOrder` at finalize time); duplicate names both kept; a 0-byte file is accepted with its size shown as "0 B".

## Exit check

- [ ] `remove(fileId)` updates the list and recomputes `overall.totalBytes` immediately (AC-001-3 first block)
- [ ] Two files with the same name can both be staged; both survive to finalize payload order
- [ ] A 0-byte file stages without error and shows "0 B"

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (wa.web: Vite 5 + React 18 + TS strict, Zustand already installed).
Task T-001-01 — add the UploadEngine store.
Read first (only): docs/features/Phase 0-MVP/F-TRF-001/US-001-02-stage-files/US-001-02-stage-files.md (happy path + Alternative flows) and F-TRF-001-upload-surface.md (FR-001-2).
Do exactly:
1. Add src/wa.web/src/core/upload/UploadEngine.ts — a Zustand store with the exact TA-8.3 state shape { files: [{id, name, size, status, progress}], overall: {sentBytes, totalBytes} } and API start(draft), retry(fileId), remove(fileId), reset().
2. Implement staging only: add/remove while status stays queued; start/retry are stubs flipping status (the block loop lands in T-001-07).
3. Keep selection order as SortOrder; duplicate names both kept; 0-byte files accepted and shown as "0 B".
Done when: AC-001-3's first Gherkin block holds — removing one of two staged files updates the list and total immediately, duplicates survive, and a 0-byte file stages.
Constraints: TA-8.3 is the contract — do not invent extra state fields; no @azure/storage-blob import yet (T-001-07 adds it); no server round-trip at staging time.
```
