# US-001-01 — Select files with drag & drop, click, or paste

**Feature:** F-TRF-001 — Upload Surface | **Status:** pending

---

**Story:** As a guest sender, I want to select my files by dragging them onto the page, clicking a button, or pasting an image, so that I can start a transfer in under five seconds without an account.
**Actor:** Guest (no account) or signed-in user, on the landing page (`/`).
**Goal:** Get files into the staging list using the most convenient method (desktop: drag; mobile: file picker; clipboard: paste).

## Preconditions

- Landing page loaded.
- The page shows the full-viewport drop zone (FR-001-1).

## Happy path

1. User drags 3 files onto the drop zone (or clicks **Choose files**, or pastes an image).
2. Drop zone highlights on drag-over (solid `--accent` border, `--accent-soft` background).
3. Each file appears in the staging list with its name, size, and a remove control.
4. The total line updates: "N files · X GB".

## Alternative flows

- **Paste:** an image on the clipboard is added as one file (name derived, e.g. `pasted-image.png`).
- **Click-to-browse:** native file picker; multi-select; on mobile this is the primary path (F-TRF-017-1).
- **Drag a folder:** files inside are flattened; empty folders are skipped with a toast "Some files were skipped" (EC-001-4).

## Acceptance criteria

```gherkin
Given the landing page is open
When I drag 3 files (total 2 GB) onto the drop zone
Then all 3 files appear in the staging list with correct names and sizes
And the total line shows the combined size
And no upload has started yet (no bytes written)

Given the page is open and I have an image on my clipboard
When I press Ctrl+V
Then the image appears in the staging list

Given I click "Choose files" on a phone
When I pick 2 files from the file picker
Then both appear in the staging list
```

## Edge cases

- Dropping a renamed-on-disk file: upload continues on the `File` object (EC-001-1); UI copy says "we copy the file now".
- HEIC or unknown MIME types are accepted without sniffing (EC-001-4).

## UI notes

- Drop zone per UI-Reference §4.1: dashed border idle, solid accent on drag-over; visible **Choose files** button (keyboard path, F-TRF-016-2).
- ARIA: drop zone `role=button` with localized `aria-label` "Upload files".
- Tone: "Send your files." — no exclamation points.

## Technical notes

- Staging is client-side only; files are not uploaded until "Send." (progress engine `UploadEngine`, TA-8.3).
- Folder flattening via `webkitRelativePath` (no `webkitGetAsEntry` required).
- Telemetry `upload_started` is emitted at *draft creation* (first "Send."), not at selection.

## Links

- Feature: `TRF-001-upload-surface.md` (FR-001-1, FR-001-2, EC-001-1/4)
- Plan ACs: AC-001-1 (partial), AC-001-4
- Architecture: TA-4.2#1, TA-8.2/8.3
- Design: UI-Reference §4.1, §5.1
- Milestone: T-012
