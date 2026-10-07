# T-001-08 — Per-block retry with backoff + row-local Retry (FR-001-6)

**Story:** US-001-05 | **Spec:** FR-001-6, AC-001-3, TA-8.3/TA-10.2 | **Size:** M
**Depends on:** T-001-06 (block upload engine — the retry wraps its block loop)

---

## Context to read (only these)

- `US-001-05-retry-upload.md` → happy path + Alternative flows + Technical notes
- `../../F-TRF-001-upload-surface.md` → FR-001-6 + AC-001-3 + Test plan (unit line)

## Instructions

1. Wrap each block upload in a retry loop: up to **5 attempts** with exponential backoff 0.5 s, 1 s, 2 s, 4 s, 8 s (capped); block IDs are deterministic (`fileId + blockIndex`) so re-issuing a block is idempotent at the blob level.
2. If all 5 fail: the file row turns `--danger` ("Upload failed — retry") with a **Retry** button; other files keep uploading (row-local failure, no modal).
3. **Retry** resumes from the last completed block — finished blocks are never re-uploaded (block-level resume within session).
4. Whole-network loss: all in-flight files fail individually; overall state is "paused", not "failed"; a first-failure toast "Some files need attention" appears once per session.
5. Emit **`upload_failed`** per file with `retries` and last error code (TA-10.2).

## Exit check

- [ ] A block failing 5 times marks the row failed with Retry; sibling files continue unaffected (AC-001-3)
- [ ] Pressing Retry resumes from the last completed block — no finished bytes re-uploaded
- [ ] Unit tests cover the retry/backoff counter and the state machine transitions queued→uploading→done/failed

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (wa.web: Vite 5 + React 18 + TS strict).
Task T-001-08 — add block-level retry.
Read first (only): docs/features/Phase 0-MVP/F-TRF-001/US-001-05-retry-upload/US-001-05-retry-upload.md (happy path + Alternative flows + Technical notes) and F-TRF-001-upload-surface.md (FR-001-6).
Do exactly:
1. Wrap each block upload in a retry loop: up to 5 attempts with exponential backoff 0.5 s, 1 s, 2 s, 4 s, 8 s (capped); block IDs deterministic (fileId + blockIndex) so re-issuing is idempotent at the blob level.
2. After all 5 fail: row turns --danger ("Upload failed — retry") with a Retry button; other files keep uploading (row-local failure, no modal).
3. Retry resumes from the last completed block — finished blocks are never re-uploaded within the session.
4. Whole-network loss: each in-flight file fails individually; overall state is "paused", not "failed"; first-failure toast "Some files need attention" once per session.
5. Emit upload_failed per file with retries and last error code (TA-10.2).
Done when: AC-001-3 holds — 5 failed attempts surface a row-local Retry, siblings continue, and retry resumes without re-uploading finished bytes.
Constraints: backoff schedule exactly as specified (unit-testable); no global failure state — failures are per-file; upload_failed template matches TA-10.2 exactly.
```
