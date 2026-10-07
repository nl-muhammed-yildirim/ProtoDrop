# T-001-02 — Drop zone: drag, click-to-browse, paste (FR-001-1)

**Story:** US-001-01 | **Spec:** FR-001-1, AC-001-4 (skip toast), EC-001-1/4 | **Size:** M
**Depends on:** T-001-01 (UploadEngine store — files land in it)

---

## Context to read (only these)

- `US-001-01-select-files.md` → happy path + Alternative flows + UI notes
- `../../F-TRF-001-upload-surface.md` → FR-001-1 + EC-001-1/4 + UI notes (drop zone line)

## Instructions

1. Build the full-viewport drop zone in **`src/wa.web/src/features/landing/DropZone.tsx`**: drag-and-drop, visible **Choose files** button (`<input type=file multiple>`), and clipboard paste (Ctrl+V → image added as one file, name derived e.g. `pasted-image.png`).
2. Drag-over styling per UI-Reference §4.1: dashed `--border` idle → solid `--accent` border + `--accent-soft` background on drag-over.
3. ARIA: drop zone `role=button` with localized `aria-label` "Upload files".
4. Dropping a folder flattens its files via `webkitRelativePath`; empty folders are skipped with the toast **"Some files were skipped"** (AC-001-4).

## Exit check

- [ ] Dragging 3 files onto the zone adds all 3 to the staging list with correct names and sizes; no upload starts (no bytes written)
- [ ] Pasting an image adds one file named `pasted-image.png`
- [ ] Dropping a folder flattens it; empty folders produce the skip toast

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (wa.web: Vite 5 + React 18 + TS strict).
Task T-001-02 — build the drop zone.
Read first (only): docs/features/Phase 0-MVP/F-TRF-001/US-001-01-select-files/US-001-01-select-files.md (happy path + Alternative flows + UI notes) and F-TRF-001-upload-surface.md (FR-001-1).
Do exactly:
1. Build the full-viewport drop zone in src/wa.web/src/features/landing/DropZone.tsx: drag-and-drop, a visible Choose files button (<input type=file multiple>), and clipboard paste (Ctrl+V → one file, name derived e.g. pasted-image.png).
2. Drag-over styling per UI-Reference §4.1: dashed --border idle → solid --accent + --accent-soft background on drag-over.
3. ARIA: role=button with localized aria-label "Upload files".
4. Folder drops flatten via webkitRelativePath; empty folders skipped with the toast "Some files were skipped" (AC-001-4).
Done when: all three AC-001-4/US-001-01 Gherkin blocks hold — drag, paste, and file-picker paths each add files to staging without starting an upload.
Constraints: accept any MIME — do not sniff (EC-001-4); no @azure/storage-blob; staging is client-side only (no bytes written until "Send.").
```
