# US-001-02 — Stage and remove multiple files

**Feature:** F-TRF-001 — Upload Surface | **Status:** pending

---

**Story:** As a sender, I want to add several files to a single transfer and remove any of them before sending, so that I can correct a wrong selection without starting over.
**Actor:** Guest or signed-in user with one or more files in the staging list.
**Goal:** Build a multi-file transfer list and keep control over its contents until "Send." is pressed.

## Preconditions

- At least one file is staged (or the user is about to add one).

## Happy path

1. User adds files one by one (or in batches); each is appended to the list in selection order.
2. The total line updates after every add/remove.
3. User hovers/taps a row and presses remove (✕); the file leaves the list and the total shrinks.
4. When the list is non-empty, **Send.** is available.
5. Sending uploads all staged files in list order.

## Alternative flows

- **Duplicate names:** two files with the same name can both be staged; both are kept and names are preserved (EC-001-2).
- **0-byte file:** accepted, but the row shows a "0 B" flag so the sender is not surprised (EC-001-3).
- **Re-ordering:** not supported in MVP (selection order is upload order).

## Acceptance criteria

```gherkin
Given the staging list has 2 files
When I remove one file
Then the list shows 1 file and the total size updates immediately
And "Send." remains enabled

Given I stage two files named "report.pdf"
When I press "Send."
Then both files are uploaded and both names are preserved in the transfer
```

## Edge cases

- Removing the last file returns the UI to the empty drop-zone state.
- Removing a file whose upload already started (post-"Send." state) is not possible — the list locks into upload state.

## UI notes

- File rows per UI-Reference §4.2: 44 px min height, icon, name (truncated, `title` attr), size, remove ✕.
- Total line: `--fs-small`, `--fg-muted`; "N files · X GB".
- **Send.** primary button disabled only when the list is empty or a limit violation is active (US-001-03).

## Technical notes

- Staging state lives in the UploadEngine Zustand store: `{ files: [{id, name, size, status, progress}], overall }` (TA-8.3).
- File order = `SortOrder` in `FileItem` at finalize time.
- No server round-trip until draft creation; this keeps "accidental" sessions free.

## Links

- Feature: `TRF-001-upload-surface.md` (FR-001-2, EC-001-2/3)
- Architecture: TA-8.3 (UploadEngine contract)
- Design: UI-Reference §4.2, §5.1
- Milestone: T-012
