# T-017-01 — Mobile upload surface: file input + sticky Send. (FR-017-2, FR-017-5)

**Story:** US-017-01 | **Spec:** FR-017-2, FR-017-5, AC-017-1, EC-017-1 | **Size:** S
**Depends on:** T-003-08 (mobile layout conventions already set: single column ≤ 640 px, sticky bar pattern)

---

## Context to read (only these)

- `US-017-01-mobile-upload.md` → happy path + Alternative flows + Edge cases + UI notes
- `../F-TRF-017-mobile.md` → FR-017-2/5, AC-017-1, EC-017-1, UI notes
- `../../US-003-06-mobile-download/T-003-08-mobile-layout-sticky.md` → the sticky-bar + single-column pattern already in use

## Instructions

1. At ≤ 640 px the landing/upload surface renders **single column** with a visible **Choose files** button — drag is additional, never required; the drop zone is not a drag-only target (FR-017-2, US-017-01 happy path 1).
2. The `<input type=file>` keeps `multiple` where the OS allows (staging accumulates across successive picks — F-TRF-001-2); on iOS it surfaces **Photos / camera** (FR-017-2). Upload still flows through the same `UploadEngine` (TA-8.3) — no mobile-specific pipeline.
   ASSUMPTION: FR-017-2 names the native picker behavior but not the input's attributes; DECISION for this task: set `accept="image/*"` on the file input so iOS surfaces Photos/camera, and do not add a global paste handler (EC-017-1 — desktop-first). Record both in `Open-Decisions-and-Constants.md` Part 3.
3. **Sticky bottom Send.** with the overall progress line above it (FR-017-5) — reuses US-001-04's progress state, mobile layout per UI-Reference §5.1; the page scrolls under it with reserved bottom padding (T-003-08 pattern).
4. Staging rows: the remove control is a **visible tap target** (≥ 44 px — FR-017-4), not a hover-reveal; file names truncate at 360 px, size stays visible (FR-017-5, US-017-01 Edge cases).
5. Inputs 44 px tall with 16 px font where the upload surface has any form field (prevents iOS zoom-on-focus — FR-017-4/§3).

## Exit check

- [ ] Phone viewport ≤ 640 px: single column, visible **Choose files** button (no drag-only target); tapping it opens the native picker; on iOS Photos/camera is surfaced (FR-017-2)
- [ ] 2 photos staged → both listed with names + sizes; progress per file and overall visible; **Send.** sticky at bottom with overall progress line above (AC-017-1, FR-017-5)
- [ ] After Send: link screen reachable on the phone and the link is copyable — full guest loop works in a mobile context (AC-017-1)
- [ ] An image pasted via the iOS share sheet into the file input works as a normal staged file — no special paste code path required (EC-017-1)
- [ ] No hover-dependent affordance on the upload surface; staging remove control is a visible ≥ 44 px tap target at 360 px, names truncated, sizes visible (FR-017-4/5)

## Implementation prompt

```text
Repo: C:\Users\myild\source\repos\ProtoDrop (.NET 10; wa.web Vite+React+TS).
Task T-017-01 — make the landing/upload surface mobile-first (milestone task T-027, F-TRF-017 part 1).
Read first (only): docs/features/Phase 0-MVP/F-TRF-017/US-017-01-mobile-upload/US-017-01-mobile-upload.md; F-TRF-017-mobile.md (FR-017-2/5, AC-017-1, EC-017-1, UI notes); US-003-06's T-003-08 task file for the existing sticky-bar pattern.
Do exactly: at <= 640 px the upload surface is single column with a visible Choose files button (drag additional, never required — FR-017-2); keep <input type=file multiple> through the same UploadEngine (TA-8.3), set accept="image/*" so iOS surfaces Photos/camera and add no global paste handler (EC-017-1 — record both in Open-Decisions-and-Constants.md Part 3); sticky bottom Send. with the overall progress line above it (FR-017-5, UI-Reference §5.1) using the T-003-08 reserved-padding pattern; staging remove control is a visible >= 44 px tap target, names truncate at 360 px, size stays visible.
Done when: AC-017-1 holds in a phone viewport — select 2 photos (Photos/camera on iOS) → progress visible per file and overall → Send. → link screen with copyable link (FR-017-2/5, EC-017-1).
Constraints: wa.web only; reuse the T-003-08 mobile patterns (single column, sticky bar, reserved padding); no new upload pipeline — UploadEngine is unchanged. Commit "feat: T-017-01 {one line}".
```
