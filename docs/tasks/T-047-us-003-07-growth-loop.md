# T-047 — Become a sender after downloading (growth loop)

**Story:** US-003-07 | **Feature:** F-TRF-003 | **Phase:** 0 — MVP
**Story file:** `../features/Phase 0-MVP/F-TRF-003/US-003-07-growth-loop.md`
**Coarse task (Milestone-Backlog.md):** T-014
**Status:** pending

---

## Scope

As a recipient who just got their files, I want an easy "Send something" next step, so that — in the same sitting — I can become a sender and discover the product for myself.

**Actor:** Recipient (no account) who completed a full download, or who landed on a terminal-state screen.

**Goal:** After a successful full download, a growth-loop panel invites them to send; every terminal state also carries the **Send something** CTA (FR-003-7, F-TRF-003-8/9).

Happy path:

1. **After a full download:** a panel appears below the file list: "Send something" → tapping it drops to the upload surface (F-TRF-001) with the landing drop zone ready — the recipient is now a guest sender.
2. **On terminal states:** the single "Send something" action on the expired/limit/unknown screens does the same — no file list needed.
3. The hand-off is instant: no sign-up, no "you were here" pre-fill (the recipient doesn't know the sender's files), just the drop zone (US-001-01).

## Acceptance criteria

```gherkin
Given I completed a full download of an active transfer
When the download finishes
Then a "Send something" panel appears
And tapping it takes me to the upload surface (drop zone ready, no account required)

Given I opened an expired link
When I see the expired screen
Then "Send something" is the single action button
And it leads to the upload surface

Given I downloaded only 1 of 5 files
When the download finishes
Then the "Send something" panel is NOT shown
```

## Edge cases

- Panel appears exactly once per completed full download (not re-announced on re-download).
- Telemetry distinguishes the path: `growth_cta_shown`, `growth_cta_clicked` (source = post-download vs terminal-state) — feeds the funnel dashboards (F-TRF-012).
- The recipient who clicks through is a **new** session for sending purposes; no transfer of the unlock token or state (they have no files to send yet).

## Exit check

- [ ] Scenario 1: I completed a full download of an active transfer
- [ ] Scenario 2: I opened an expired link
- [ ] Scenario 3: I downloaded only 1 of 5 files
- [ ] Only closed-list error codes / telemetry names used (TA-4.1.3 / TA-10.2)
- [ ] No new NuGet/npm package without an ADR line (golden rule 1)
- [ ] AGENT.md §4 test gate green (domain.unit, application.unit, api.integration, web lint+test+build)

## Links

- Story: `../features/Phase 0-MVP/F-TRF-003/US-003-07-growth-loop.md`
- Feature: `TRF-003-recipient-page.md` (FR-003-7, F-TRF-003-8/9)
- Related: US-001-01 (upload surface hand-off), US-003-05 (terminal states)
- Architecture: TA-10.2
- Design: UI-Reference §4.6, §5.3, §7
- Milestone: T-014
