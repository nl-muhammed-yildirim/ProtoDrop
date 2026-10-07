# T-001-07 — Progress UI: per-file % + overall bytes line (FR-001-5)

**Story:** US-001-04 | **Spec:** FR-001-5, AC-001-1 (progress half), TA-8.3/TA-10.2 | **Size:** M
**Depends on:** T-001-06 (block upload engine — progress callbacks feed the store)

---

## Context to read (only these)

- `US-001-04-upload-progress.md` → happy path + UI notes
- `../../F-TRF-001-upload-surface.md` → FR-001-5 + AC-001-1 + Technical notes (progress line)

## Instructions

1. Render per-file progress in the staging rows: 3 px `--accent` bar + percentage in `--fs-tiny`, driven live from the store (no polling).
2. Overall line above the list: "Uploading… X GB of Y GB" — combined **bytes uploaded / total bytes**, recomputed on every callback (FR-001-5: bytes, not blocks).
3. A file at 100 % shows a `--success` done check; the next file begins filling immediately after.
4. ARIA live region (`role=status`, polite) announces overall changes — not every percent tick (F-TRF-016-3).
5. When all files are done, the flow advances to the link screen (F-TRF-002 / T-013 takes over from here).

## Exit check

- [ ] Every file row shows a live percentage based on bytes uploaded; the overall line shows combined progress (AC-001-1)
- [ ] A completed row shows the done check and the next row begins filling — no polling, state is live
- [ ] The polite status region announces overall changes without spamming every tick

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (wa.web: Vite 5 + React 18 + TS strict).
Task T-001-07 — build the progress UI.
Read first (only): docs/features/Phase 0-MVP/F-TRF-001/US-001-04-upload-progress/US-001-04-upload-progress.md (happy path + UI notes) and F-TRF-001-upload-surface.md (FR-001-5).
Do exactly:
1. Render per-file progress in the staging rows: 3 px --accent bar + percentage in --fs-tiny, driven live from the store (no polling).
2. Overall line above the list: "Uploading… X GB of Y GB" — combined bytes uploaded / total bytes, recomputed on every callback (FR-001-5: bytes, not blocks).
3. A file at 100 % shows a --success done check; the next file begins filling immediately after.
4. Add an ARIA live region (role=status, polite) announcing overall changes — not every percent tick (F-TRF-016-3).
5. When all files are done, advance to the link screen (F-TRF-002 / T-013 takes over from here).
Done when: AC-001-1's progress half holds — live byte-based percentages per file and overall, done check on completion, next file starts automatically.
Constraints: read-only render of store state — no upload logic in the component; use design tokens only.
```
