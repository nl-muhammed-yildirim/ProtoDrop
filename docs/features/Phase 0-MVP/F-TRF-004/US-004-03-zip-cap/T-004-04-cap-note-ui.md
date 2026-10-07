# T-004-04 — Cap note: hide Download all + "Files are large" (FR-004-4, AC-004-2)

**Story:** US-004-03 | **Spec:** FR-004-1/4, AC-004-2, TA-4.2a | **Size:** S
**Depends on:** T-003-01 (endpoint 4 computes `hasDownloadAll`), T-003-02 (recipient page)

---

## Context to read (only these)

- `US-004-03-zip-cap.md` → happy path + Alternative flows + Edge cases
- `../../F-TRF-004-download-zip.md` → FR-004-1/4 + AC-004-2 + UI notes (cap note line)

## Instructions

1. When endpoint 4 returns **`hasDownloadAll: false`**, the recipient page renders per-file **Download** buttons only — no disabled greyed-out button, absence is the pattern (FR-004-1).
2. For the cap case (`SUM(SizeBytes) > MAX_ZIP_SIZE`) additionally show an inline note under the file list in `--fs-small` / `--fg-muted`: **"Files are large — download individually."** (AC-004-2). A single-file transfer shows no note at all.
3. The cap is "exceeds", not "equals": a transfer exactly at `MAX_ZIP_SIZE` keeps the button (the server computes this in T-003-01 via `ILimitsProvider`).
4. Per-file downloads keep working as usual on both cases (F-TRF-003, US-003-03).

## Exit check

- [ ] 6 GB transfer (cap 4 GB): no Download all button + the inline note is shown; per-file downloads work
- [ ] Single-file transfer: no Download all button and **no** cap note
- [ ] Transfer exactly at the cap: button present (equality case)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (wa.web + wa-api; endpoint 4 hasDownloadAll in place).
Task T-004-04 — handle the cap note on the recipient page.
Read first (only): docs/features/Phase 0-MVP/F-TRF-004/US-004-03-zip-cap/US-004-03-zip-cap.md (happy path + Alternative flows + Edge cases) and F-TRF-004-download-zip.md (FR-004-1/4).
Do exactly:
1. When endpoint 4 returns hasDownloadAll false, the recipient page renders per-file Download buttons only — no disabled greyed-out button, absence is the pattern (FR-004-1).
2. For the cap case (SUM(SizeBytes) > MAX_ZIP_SIZE) additionally show an inline note under the file list in --fs-small / --fg-muted: "Files are large — download individually." (AC-004-2). A single-file transfer shows no note at all.
3. Keep the cap "exceeds" not "equals": a transfer exactly at MAX_ZIP_SIZE keeps the button (the server computes this in T-003-01 via ILimitsProvider).
4. Keep per-file downloads working as usual on both cases (F-TRF-003, US-003-03).
Done when: AC-004-2 holds — the 6 GB case hides the button with the note and per-file still works, the single-file case shows neither, and the exactly-at-cap case keeps the button.
Constraints: use design tokens only; hasDownloadAll is server-computed (TA-4.2a) — do not recompute the sum client-side at M0.
```
