# US-016-02 — Hear what's happening (screen reader)

**Feature:** F-TRF-016 — Accessibility | **Status:** pending

---

**Story:** As a screen-reader user, I want the app to announce the things that happen over time — upload progress, toasts, password errors, download counts — so that nothing important is silent.
**Actor:** Screen-reader user (NVDA/VoiceOver), on the upload surface, recipient page, and toasts.
**Goal:** ARIA live regions carry the temporal updates: upload progress (polite, throttled), toasts (`role=status`/`alert`), password errors (`role=alert`), and "Downloads left" changes (polite) (FR-016-3).

## Preconditions

- A screen reader is running; the page uses the `useAriaLive` helper (TA-8.2 `core/ui`).

## Happy path

1. **Upload progress:** the overall progress is announced at **0 / 25 / 50 / 75 / 100 %** — not every tick (EC-016-1) — via a polite live region tied to the upload list.
2. **A file fails:** the failed file is announced with its **Retry** action ("{name} failed. Press Enter to retry." — the row's Retry button is focusable, US-016-01).
3. **Toasts:** success toasts use `role=status` (polite); error toasts use `role=alert` (assertive) — matching their visual channel (UI-Reference §4.3).
4. **Password error:** "Wrong password" lands in a `role=alert` region — the visual shake is a bonus, the text is the contract (FR-016-5, AC-016 pattern).
5. **Recipient page:** after a download, "Downloads left: N" changes are announced politely (US-003-05's number is not silent).

## Alternative flows

- **Zip "preparing" state:** "Preparing your download…" is announced once (not re-announced on each poll) — the button's disabled+spinner state is the visible channel (F-TRF-004).
- **Growth-loop panel appearing:** announced politely ("Send something" panel) — it's a new actionable region (US-003-07).
- **Bulk updates (e.g. 10 files fail at once):** one summary announcement ("3 files failed") + the per-row states; no 10 announcements in a row (throttle applies to *all* live content, not just progress).

## Acceptance criteria

```gherkin
  When they reach the upload
  Then they hear the overall progress updates (polite live region) at 0/25/50/75/100%
  And a failed file is announced with its Retry action

Given a wrong password is submitted
When the error is shown
Then "Wrong password" is announced via role=alert

Given a successful toast appears
When it renders
Then it is announced via role=status (polite)
```

## Edge cases

- **Throttle is a unit-tested behavior** (F-016 test plan): progress announcement points 0/25/50/75/100; a 99→100 jump is always announced (endpoints inclusive).
- **Live regions must exist before the update** (screen-reader requirement): the hidden `role=status` container is mounted with the screen, not created on first use (F-TRF-016 UI notes: one hidden container per stateful screen).
- **Polite vs assertive is content-dependent** (error = assertive, info = polite) — a single helper enforces the mapping so it can't drift per screen.

## UI notes

- Hidden live-region containers: upload list, password card (F-TRF-016 UI notes); toasts follow UI-Reference §4.3.
- Row `aria-label`s double as the announcement text ("Download {name}, {size}") — one source of truth for the spoken name.

## Technical notes

- `useAriaLive` helper: `aria-live="polite|assertive"` attributes, no third-party kit (TA-8.2).
- E2E: Playwright with an injected upload failure asserts the live-region contents during the run (F-TRF-016 test plan).
- Telemetry independence: live regions are pure UI state — no network calls on announce.

## Links

- Feature: `TRF-016-a11y.md` (FR-016-3, FR-016-5, AC-016-2, EC-016-1)
- Related: US-016-01 (keyboard access to the announced actions), US-001-05 (failed-upload state), US-003-04 (password error)
- Architecture: TA-8.2 (`core/ui`)
- Design: UI-Reference §3, §4.3
- Milestone: T-027
