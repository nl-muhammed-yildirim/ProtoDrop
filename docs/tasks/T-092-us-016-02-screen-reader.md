# T-092 — Hear what's happening (screen reader)

**Story:** US-016-02 | **Feature:** F-TRF-016 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-016/US-016-02-screen-reader.md`
**Coarse task (Milestone-Backlog.md):** T-027
**Status:** pending

---

## Scope

As a screen-reader user, I want the app to announce the things that happen over time — upload progress, toasts, password errors, download counts — so that nothing important is silent.

**Actor:** Screen-reader user (NVDA/VoiceOver), on the upload surface, recipient page, and toasts.

**Goal:** ARIA live regions carry the temporal updates: upload progress (polite, throttled), toasts (`role=status`/`alert`), password errors (`role=alert`), and "Downloads left" changes (polite) (FR-016-3).

Happy path:

1. **Upload progress:** the overall progress is announced at **0 / 25 / 50 / 75 / 100 %** — not every tick (EC-016-1) — via a polite live region tied to the upload list.
2. **A file fails:** the failed file is announced with its **Retry** action ("{name} failed. Press Enter to retry." — the row's Retry button is focusable, US-016-01).
3. **Toasts:** success toasts use `role=status` (polite); error toasts use `role=alert` (assertive) — matching their visual channel (UI-Reference §4.3).
4. **Password error:** "Wrong password" lands in a `role=alert` region — the visual shake is a bonus, the text is the contract (FR-016-5, AC-016 pattern).
5. **Recipient page:** after a download, "Downloads left: N" changes are announced politely (US-003-05's number is not silent).

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

## Exit check

- [ ] Scenario 1: a wrong password is submitted
- [ ] Scenario 2: a successful toast appears
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-016/US-016-02-screen-reader.md`
- Feature: `TRF-016-a11y.md` (FR-016-3, FR-016-5, AC-016-2, EC-016-1)
- Related: US-016-01 (keyboard access to the announced actions), US-001-05 (failed-upload state), US-003-04 (password error)
- Architecture: TA-8.2 (`core/ui`)
- Design: UI-Reference §3, §4.3
- Milestone: T-027
