# T-017-02 — Mobile download mechanics verification (Safari zip bar, original filenames)

**Story:** US-017-02 | **Spec:** FR-017-3/5, AC-017-2, EC-017-2/4 | **Size:** S
**Depends on:** T-003-08 (recipient mobile layout), T-004-03 (Download all UI), T-003-06/T-004-01 (per-file + zip SAS delivery)

---

## Context to read (only these)

- `US-017-02-mobile-download.md` → happy path + Alternative flows + Edge cases
- `../F-TRF-017-mobile.md` → FR-017-3/5, AC-017-2, EC-017-2/4

## Instructions

1. **Delivery is server-identical to desktop** (30-min read SAS on the file / `all.zip` — FR-017-3); this task verifies and fixes only where mobile surfaces differ: iOS Safari's visible download bar, Android Chrome's Downloads folder (EC-017-4), filename survival.
2. **Per-file downloads:** verify in a mobile context that the saved file keeps its **original filename** — the SAS URL/`Content-Disposition` must carry it; no `.bin`, no linkId suffix (FR-017-3, US-017-02 AC "per-file download on iOS").
3. **Download all:** verify Safari shows the zip in its visible download bar and the extracted files keep original top-level names (zip entries are original names — FR-004-5); Android Chrome lands it in the Downloads folder with per-file buttons remaining the documented fallback (EC-017-4).
4. **Fallbacks:** single-file transfers show only per-file **Download** (no Download all, FR-004-1); iOS cancels the zip → per-file buttons remain the fallback with no error state left behind (US-003 EC, EC-004-4); large zip near `MAX_ZIP_SIZE` hides the button with the inline note, readable on a phone, per-file download is the path (US-004-03).
5. Sticky bottom **Download / Download all** already in place from T-003-08 — verify it holds on this page's states (generating: "Preparing your download…" spinner + disabled) without re-layout work.
6. 4G flaky downloads rely on browser-native Range resume against the SAS URL (EC-017-2, EC-003-1) — spot-check manually in staging; no code required unless a bug is found.

## Exit check

- [ ] Per-file download in a mobile context saves with the original filename (FR-017-3; US-017-02 gherkin 2)
- [ ] "Download all" on a 2-file transfer: Safari shows the zip in its visible download bar; extracted names survive (AC-017-2)
- [ ] Android Chrome context: zip lands in Downloads folder; per-file buttons remain the fallback (EC-017-4; US-017-02 gherkin 3)
- [ ] Single-file transfer shows only per-file Download (FR-004-1); cancelled iOS zip leaves no error state, per-file buttons work (EC-004-4)
- [ ] Zip-over-cap inline note readable at 360 px; per-file download is the path (US-004-03 mobile half)
- [ ] Range-resume spot-check on 4G passes in staging (EC-017-2, browser-native — no code change expected)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS).
Task T-017-02 — verify and fix mobile download mechanics (milestone task T-027, F-TRF-017 part 2).
Read first (only): docs/features/Phase 0-MVP/F-TRF-017/US-017-02-mobile-download/US-017-02-mobile-download.md and F-TRF-017-mobile.md (FR-017-3/5, AC-017-2, EC-017-2/4).
Do exactly: verify per-file downloads in mobile contexts keep the original filename (SAS URL/Content-Disposition must carry it — FR-017-3); verify "Download all" on a 2-file transfer shows Safari's visible download bar with original top-level zip entries (AC-017-2, FR-004-5) and lands in Android Chrome's Downloads folder (EC-017-4); confirm single-file transfers show only per-file Download (FR-004-1), a cancelled iOS zip leaves no error state with per-file buttons as fallback (EC-004-4), and the zip-over-cap inline note is readable at 360 px (US-004-03); spot-check browser-native Range resume on 4G in staging (EC-017-2/EC-003-1). Fix only real mobile-surface bugs found — delivery is server-identical to desktop.
Done when: AC-017-2 holds in the iPhone Playwright context where the browser exposes the download, and the manual staging pass (Safari + Chrome) confirms original filenames and the zip download bar (FR-017-3).
Constraints: wa.web + at most Content-Disposition handling on the existing SAS endpoints — no new delivery pipeline; desktop behavior unchanged. Commit "feat: T-017-02 {one line}".
```
