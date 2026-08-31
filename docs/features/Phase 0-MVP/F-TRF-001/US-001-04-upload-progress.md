# US-001-04 — Watch per-file and overall progress

**Feature:** F-TRF-001 — Upload Surface | **Status:** pending

---

**Story:** As a sender uploading a 4 GB video, I want to see exactly how much of each file and of the whole transfer has been uploaded, so that I never guess whether the transfer is working.
**Actor:** Any user who has pressed "Send." and is uploading staged files.
**Goal:** Turn a long, silent network operation into a visible, byte-accurate progress display.

## Preconditions

- Draft created (`POST /transfers/draft`), per-file SAS URLs received.
- UploadEngine is running (files serialized, 4 parallel blocks per file, 8 MiB blocks — TA-8.3).

## Happy path

1. After "Send.", each file row shows a 3 px progress bar and a percentage.
2. An overall line shows combined uploaded bytes vs total bytes ("1.2 GB of 4.0 GB").
3. Percentages are computed from **bytes uploaded**, not block count (FR-001-5).
4. When a file hits 100 % it is marked done (check icon) and the next file starts.
5. When all files are done, the overall line shows 100 % and the flow advances to the link screen (F-TRF-002).

## Alternative flows

- **Paused tab:** progress continues in the background (block-blob upload survives tab sleep); on return the bars reflect actual bytes.
- **Slow connection:** no timeout on the overall upload; only block-level retries (US-001-05) apply.

## Acceptance criteria

```gherkin
Given I have 3 files staged and I press "Send."
When the upload runs
Then every file row shows a live percentage based on bytes uploaded
And the overall line shows combined progress
When all files reach 100%
Then "Send." advances to the link screen

Given a file row is at 100%
When the next file starts
Then the completed row shows a done check and the next row begins filling
```

## Edge cases

- The progress state is live (Zustand store), so no polling.
- A failed file pauses only its own row (US-001-05); overall % keeps counting the completed bytes.
- If the user leaves the page mid-upload, the session is lost in MVP (block-level resume is within-session only, FR-001-6).

## UI notes

- Per-row bar: 3 px, `--accent`, percent in `--fs-tiny` (UI-Reference §4.2).
- Overall line above the list: "Uploading… 1.2 GB of 4.0 GB" in `--fs-small`.
- ARIA live region (`role=status`, polite) announces overall changes, not every percent tick (F-TRF-016-3).
- Done row: `--success` check icon, name, size.

## Technical notes

- `@azure/storage-blob` `BlockBlobClient` `uploadData` with `uploadInBlocks=true` (8 MiB, maxParallel 4); progress from block callbacks.
- Overall = sum of per-file sentBytes / sum of totalBytes — recomputed on every callback.
- Telemetry `upload_completed` carries `bytes`, `durationMs`, `retries` per file and overall (TA-10.2).

## Links

- Feature: `TRF-001-upload-surface.md` (FR-001-4, FR-001-5, FR-001-7)
- Plan AC: AC-001-1
- Architecture: TA-8.3 (UploadEngine), TA-4.2#1, TA-10.2
- Design: UI-Reference §4.2, §5.1
- Milestone: T-012
