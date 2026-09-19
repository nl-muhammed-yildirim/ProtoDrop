# T-094 — Send files from my phone

**Story:** US-017-01 | **Feature:** F-TRF-017 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-017/US-017-01-mobile-upload.md`
**Coarse task (Milestone-Backlog.md):** T-027
**Status:** pending

---

## Scope

As a sender on a phone, I want to select files (camera, Photos, Files) and send a transfer entirely from my phone, so that I'm not blocked until I find a computer.

**Actor:** Guest or signed-in user on mobile Safari (iOS) or Chrome (Android), on the landing/upload surface.

**Goal:** the native file input (camera/Photos on iOS) is the upload path; no desktop drag-and-drop assumed; progress and **Send.** behave correctly on a phone.

Happy path:

1. User opens the landing on their phone: the drop zone shows the **Choose files** button (not a drag-only target).
2. Tapping **Choose files** opens the native picker; on iOS this surfaces **Photos / camera** (the file input's `accept`/capture behavior — FR-017-2).
3. User picks 2 photos; both appear in the staging list with names and sizes.
4. Progress is visible per file and overall (bytes-based, FR-001-5) — the **Send.** button is sticky at the bottom with the overall progress line above it (FR-017-5).
5. User presses **Send.**, lands on the link screen, copies the link — the whole loop works on the phone.

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

## Exit check

- [ ] Scenario 1: a phone viewport
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-017/US-017-01-mobile-upload.md`
- Feature: `TRF-017-mobile.md` (FR-017-1, FR-017-2, FR-017-4, FR-017-5, AC-017-1)
- Related: US-001-01 (selection), US-001-04 (progress), US-002-01 (link)
- Architecture: TA-8.3, TA-8.4, TA-15
- Design: UI-Reference §3, §5.1
- Milestone: T-027
