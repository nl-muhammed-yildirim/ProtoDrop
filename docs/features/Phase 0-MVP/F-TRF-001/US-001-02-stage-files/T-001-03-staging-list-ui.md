# T-001-03 — Staging list UI: rows, remove ✕, total line, "Send." button

**Story:** US-001-02 | **Spec:** FR-001-2, AC-001-3 (first block), EC-001-2/3 | **Size:** M
**Depends on:** T-001-01 (UploadEngine store), T-001-02 (drop zone feeding it)

---

## Context to read (only these)

- `US-001-02-stage-files.md` → happy path + Alternative flows + UI notes
- `../../F-TRF-001-upload-surface.md` → FR-001-2 + EC-001-2/3 + UI notes (staging list line)

## Instructions

1. Build **`StagingList.tsx`** in `features/landing/`: one row per staged file — icon, name (truncated with `title` attr), size via the existing `formatBytes()` helper, remove ✕ control; rows ≥ 44 px min height (UI-Reference §4.2).
2. Total line under the list: `"N files · X GB"` in `--fs-small`, `--fg-muted` — recomputed on every add/remove from store state.
3. Footer **Send.** primary button, enabled only when the list is non-empty (the limit-violation disable arrives with T-001-04).
4. Removing the last file returns the UI to the empty drop-zone state; duplicate names render as two rows (EC-001-2); a 0-byte row shows "0 B" (EC-001-3).

## Exit check

- [ ] Removing one of two staged files updates the list and total size immediately, **Send.** stays enabled (AC-001-3 first block)
- [ ] Two same-named files both render; removing either removes only that row
- [ ] Emptying the list returns to the empty drop-zone state

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (wa.web: Vite 5 + React 18 + TS strict).
Task T-001-03 — build the staging list UI.
Read first (only): docs/features/Phase 0-MVP/F-TRF-001/US-001-02-stage-files/US-001-02-stage-files.md (happy path + Alternative flows + UI notes) and F-TRF-001-upload-surface.md (FR-001-2).
Do exactly:
1. Build StagingList.tsx in src/wa.web/src/features/landing/: one row per staged file — icon, name (truncated with title attr), size via the existing formatBytes() helper, remove ✕ control; rows ≥ 44 px min height (UI-Reference §4.2).
2. Total line under the list: "N files · X GB" in --fs-small/--fg-muted, recomputed on every add/remove from store state.
3. Footer Send. primary button, enabled only when the list is non-empty (limit-violation disable arrives with T-001-04).
4. Removing the last file returns to the empty drop-zone state; duplicate names render as two rows (EC-001-2); a 0-byte row shows "0 B" (EC-001-3).
Done when: AC-001-3's first Gherkin block holds in the browser — remove one of two files, list and total update immediately, Send. stays enabled.
Constraints: render-only — no upload logic here; state comes from UploadEngine (T-001-01); use design tokens, no hardcoded colors.
```
