# US-017-01 — Send files from my phone

**Feature:** F-TRF-017 — Mobile Web | **Status:** pending

---

**Story:** As a sender on a phone, I want to select files (camera, Photos, Files) and send a transfer entirely from my phone, so that I'm not blocked until I find a computer.
**Actor:** Guest or signed-in user on mobile Safari (iOS) or Chrome (Android), on the landing/upload surface.
**Goal:** the native file input (camera/Photos on iOS) is the upload path; no desktop drag-and-drop assumed; progress and **Send.** behave correctly on a phone.

## Preconditions

- The landing page renders in a phone viewport (≤ 640 px → single column, FR-017-5).
- The user has files on the device (camera roll / Photos / Files) or takes a photo.

## Happy path

1. User opens the landing on their phone: the drop zone shows the **Choose files** button (not a drag-only target).
2. Tapping **Choose files** opens the native picker; on iOS this surfaces **Photos / camera** (the file input's `accept`/capture behavior — FR-017-2).
3. User picks 2 photos; both appear in the staging list with names and sizes.
4. Progress is visible per file and overall (bytes-based, FR-001-5) — the **Send.** button is sticky at the bottom with the overall progress line above it (FR-017-5).
5. User presses **Send.**, lands on the link screen, copies the link — the whole loop works on the phone.

## Alternative flows

- **Camera capture (iOS):** taking a photo in-app lands it as one file in staging — same as a picked file (FR-017-2).
- **Multi-select:** where the OS allows, the picker supports multiple files; where it doesn't, the user can add files in successive picks (staging list accumulates — F-TRF-001-2).
- **A file too large:** the size-limit warning (US-001-03) shows before upload starts; no bytes written.

## Acceptance criteria

```gherkin
  When they open the landing page
  Then they can select 2 photos from Photos
  And upload completes (progress visible)
  And they get a link and can copy it

Given a phone viewport
When the landing renders
Then the "Choose files" button is present (no drag-only target)
And the Send button is sticky at the bottom with the overall progress line above it
```

## Edge cases

- **iOS paste into the file input:** not required (clipboard paste is desktop-first); a pasted image via the share sheet works as a normal file (EC-017-1).
- **Interrupted upload (app backgrounded):** the block-level retry/resume from F-TRF-001-6 applies; the staging list and progress survive a page reload within the session (block-level resume).
- **Small screens (360 px):** no horizontal scroll; file names truncate with a tooltip-equivalent (long-press / `aria-label`), size stays visible (FR-017-5).

## UI notes

- Sticky bottom **Send.** with the overall progress line above it (FR-017-5, UI-Reference §5.1 mobile layout).
- The file input is 44 px tall with 16 px font (prevents iOS zoom-on-focus, FR-017-4/§3).
- No hover-dependent affordances (F-TRF-017-5): the staging row's remove control is a visible tap target, not a hover-reveal.

## Technical notes

- Upload is the same `UploadEngine` (TA-8.3) — mobile is an I/O path, not a separate pipeline; the file input is the selection surface (FR-017-2).
- Performance budget on mobile (TA-8.4): JS < 300 KB gz; TTI < 1 s on 4G for `/`.
- E2E: Playwright mobile context (iPhone profile) — select-from-Photos simulated via the file input (F-TRF-017 test plan, T-027).

## Links

- Feature: `TRF-017-mobile.md` (FR-017-1, FR-017-2, FR-017-4, FR-017-5, AC-017-1)
- Related: US-001-01 (selection), US-001-04 (progress), US-002-01 (link)
- Architecture: TA-8.3, TA-8.4, TA-15
- Design: UI-Reference §3, §5.1
- Milestone: T-027
