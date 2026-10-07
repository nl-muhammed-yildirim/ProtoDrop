# US-001-05 — Recover from a failed upload

**Feature:** F-TRF-001 — Upload Surface | **Status:** pending

---

**Story:** As a sender on an unstable connection, I want a failed file to be retried automatically and, if it still fails, to be able to retry it with one click, so that one flaky block does not kill a 4 GB transfer.
**Actor:** Any user whose upload encountered block failures.
**Goal:** Isolate failures to the failing file and keep everything else intact.

## Preconditions

- Upload in progress (US-001-04).

## Happy path

1. A block fails (network error / 408 / 5xx from the blob endpoint).
2. UploadEngine retries **that block** with exponential backoff, up to 5 attempts (FR-001-6).
3. If a retry succeeds, the file continues normally — the user may not even notice.
4. If all 5 fail, the file row turns `--danger` ("Upload failed — retry") with a **Retry** button; other files keep uploading.
5. **Retry** resumes the file from the last completed block (successful blocks are kept — block-level resume within session).
6. After all files are done (or re-tried to done), the flow advances as in US-001-04.

## Alternative flows

- **Whole-network loss:** all in-flight files fail; each shows **Retry**; overall state is "paused", not "failed".
- **User abandons:** an abandoned session's staging blobs expire via the 24 h lifecycle rule (TA-3.5) — no leak.
- **Retry after "Send." screen:** if the user navigates forward, failed files remain in the staging list on back-navigation (session-scoped).

## Acceptance criteria

```gherkin
Given the network drops while a block is uploading
When the block fails 5 times with backoff
Then the file row is marked failed with a Retry button
And the other files continue uploading unaffected

Given a file is marked failed
When I press Retry
Then the file resumes from its last completed block
And reaches 100% without re-uploading finished blocks

Given 3 files are staged and the network is down
When all 3 fail
Then the overall state is "paused" and each row offers Retry
```

## Edge cases

- Retry counter and backoff are per block, per file: 5 attempts → 0.5 s, 1 s, 2 s, 4 s, 8 s (capped).
- A file that failed *and* was removed from staging is dropped from the plan; the draft is created from the remaining files (if any).
- Telemetry: `upload_failed` per file with `retries` and last error code (TA-10.2).

## UI notes

- Failed row: `--danger` text + secondary **Retry** button (UI-Reference §4.2).
- No modal, no blocking dialog — the failure is row-local.
- Toast on first failure of a session: "Some files need attention" (info, not error, since others continue).

## Technical notes

- `uploadData` block callback returns the error; UploadEngine wraps a per-block retry loop (5 × backoff) before surfacing failure.
- Block IDs are deterministic (`fileId + blockIndex`), so re-issuing a block is idempotent at the blob level.
- On finalize, only files at 100 % are committed; a draft with zero done files → "Send." stays disabled.

## Links

- Feature: `TRF-001-upload-surface.md` (FR-001-6)
- Plan AC: AC-001-3
- Architecture: TA-8.3, TA-3.5 (staging lifecycle), TA-10.2
- Design: UI-Reference §4.2
- Milestone: T-012
