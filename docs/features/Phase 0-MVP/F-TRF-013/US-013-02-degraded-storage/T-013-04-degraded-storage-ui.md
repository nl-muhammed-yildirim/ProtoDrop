# T-013-04 — Degraded-storage row states: retry hint + per-file unavailable (FR-013-3, AC-013-2)

**Story:** US-013-02 | **Spec:** FR-013-3 / US-013-02 AC gherkin (both blocks) + edge cases; UI per F-TRF-013 UI notes | **Size:** S
**Depends on:** T-013-03 (API returns the 503+`INTERNAL` row-level states)

---

## Context to read (only these)

- `US-013-02-degraded-storage.md` → Happy path + Alternative flows + Edge cases + UI notes
- `../F-TRF-013-errors.md` → FR-013-3 row + AC-013-2
- F-TRF-001's failed-row treatment (T-009-05-era upload rows) in the existing upload screen — reuse, don't restyle

## Instructions

1. **Upload rows:** when a block-upload failure comes back as T-013-03's 503 + `code INTERNAL` after the retry ladder, show that file's row: "Storage temporarily unavailable — retry." in `--danger` text + a per-file **Retry** ghost button (matches F-001's failed-row treatment). Retry re-runs the block upload for just that file; on success the row state resets (EC-013-2).
2. **Download rows:** when a mint fails with the same mapping, show "File temporarily unavailable." in that row only — the page still lists all files and other rows remain downloadable (FR-013-3); no global banner in MVP.
3. **Whole surface down:** if every file fails the rows simply all show their states + **Retry** — there is *no* global "everything is broken" screen in MVP (the 500 screen is reserved for API-level failures, US-013-02 Alternative flows).
4. Announcement: row state text is the single channel plus an `aria-live` polite announcement (F-TRF-016-3) — no double-announce.

## Exit check

- [ ] AC-013-2 block 1: after retries the failing row shows "Storage temporarily unavailable — retry." + per-file **Retry**; other files' rows are unaffected; a successful retry clears the state (EC-013-2)
- [ ] AC-013-2 block 2: mint failure → that row shows "File temporarily unavailable."; the file list still renders and other rows remain actionable
- [ ] FR-013-3: no global degradation banner anywhere; states are row-local and self-clearing on success

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (src/wa.web).
Task T-013-04 — degraded-storage row states UI (milestone task T-025, scope "degraded states for Blob/email down").
Read first (only): US-013-02-degraded-storage.md (all sections), F-TRF-013-errors.md (FR-013-3 + AC-013-2), the existing upload failed-row treatment.
Do exactly:
1. Upload rows: 503+INTERNAL after the ladder → "Storage temporarily unavailable — retry." (--danger) + per-file Retry ghost button; success resets row state.
2. Download rows: mint failure → "File temporarily unavailable." in that row only; list still renders, other rows actionable.
3. aria-live polite announcement with the row text; no global banner; all copy as translation keys.
Done when: AC-013-2's both gherkin blocks hold — upload rows show the retry hint after retries and download rows show the per-file unavailable state, each self-clearing on success (FR-013-3, EC-013-2).
Constraints: row-local states only; reuse F-001 failed-row styling; no new screens or global indicators.
```
